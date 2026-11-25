# تصميم نهائي (للمراجعة): Deadlock 1205 في البيع واستلام المخزون
الحالة: تصميم فقط. مفيش كود ولا قاعدة بيانات اتغيّرت. READ_COMMITTED_SNAPSHOT مقفول.
الدليل: 30 deadlock graph حقيقي من SQL Server 2025 Developer على GitHub، Run 45664212707 (Artifact: sqlserver-deadlock-diagnostic، ملف deadlock-graphs.xml).
كل العمليات المشاركة كانت READ COMMITTED العادي. في الـ 30 تكرار (SC04+SC05+SC10): 17 من 30 تكرار فشلوا في الـ Run الأول (10 SC04، 6 SC05، 3 SC10).

## 1) أنواع الـ Graphs (من الأدلة)
| النوع | عدد | الموارد | الاختبار |
|---|---|---|---|
| A | 15 | Products.PK_Products (X) ضد StockReceipts.UX_StockReceipts_CreatedById_IdempotencyKey (X)، فيه طلبات منتظرة S | SC10 |
| B | 6 | Products.PK_Products (X) ضد Sales.UX_Sales_IdempotencyKey (X)، فيها حالة 3 عمليات في دورة واحدة | SC05 |
| C | 9 | CashTransactions (PK أو IX_CashTransactions_ShiftId_CreatedAt) (X) ضد Shifts.PK_Shifts (U)، منهم 3 فيهم Products كمان | SC04 |

## 2) السبب الجذري لكل نوع
**النوع C (قارئ ضد كاتب):** الضحية في أغلب الـ graphs كانت الـ SELECT اللي بيحمّل Shift مع كل CashTransactions
(`ShiftRepository.GetWithCashAsync`: SELECT TOP(1) من Shifts LEFT JOIN CashTransactions) بـ lock من نوع S. الكاتب (دفعة SaveChanges بتاعة بيع تاني)
ماسك X على صف في CashTransactions (INSERT) وبيطلب U/X على صف Shifts (تحديث ExpectedCash وRowVersion). القارئ ماسك S على صف Shifts أو فهرس CashTransactions
ومستني. دورة. السبب إن القراءة بتتم خارج أي transaction (READ COMMITTED بياخد ويسيب S locks أثناء الـ join) والحفظ بيمسك X على جدول وبيطلب التاني.

**النوع B وA (نفس الـ Idempotency Key):** طلبين بنفس المفتاح بيعدّوا الفحص الأول (`GetByIdempotencyKey` بيرجّع null لأن ولا واحد عمل commit)،
فبيدخلوا الاتنين الـ SaveChanges. كل واحد بيمسك X على جزء (صف المنتج بالـ UPDATE أو مفتاح الـ unique index بالـ INSERT) وبيطلب الجزء اللي مع التاني؛
الـ unique index كمان بيطلب S لفحص التكرار. النتيجة cycle بين Products والـ unique index. ترتيب الوصول لـ Products ومفتاح الـ Idempotency مش مضمون
ثابت بين الطلبين (داخل دفعة EF الواحدة وبين Sale وStockReceipt). تحذير: الـ graph بيحدد الموارد والأنواع بدقة، لكن ترتيب أوامر الدفعة الداخلي (executionStack كان unknown) استنتاج مش مثبت جملة جملة.
المهم إن النوعين A وB بيحصلوا بس لما طلبين بنفس المفتاح بيتسابقوا، وده بالظبط السيناريو اللي الـ Idempotency المفروض يحميه.

## 3) التصميم المقترح (مسارين مستقلين زي ما طلبت)

### المسار 1: Products / Idempotency (يعالج A وB)
1. **قفل منطقي لكل مفتاح قبل أي عمل:** داخل transaction صريحة في أول الـ Handler (Sale وStockReceipt): `sp_getapplock` بنطاق Transaction على المورد
   `idem:<النوع>:<المستخدم>:<المفتاح>` بـ timeout قصير. أول طلب بياخد القفل وبينفذ، الطلب التاني بيستنى القفل.
2. بعد القفل مباشرة (جوه نفس الـ transaction): نعيد `GetByIdempotencyKey`. لو لقى النتيجة الأصلية (بعد ما الأول عمل commit) يرجّعها
   ويتنهى الطلب التاني من غير أي كتابة. لو ملقاش، يكمّل التنفيذ. كده العملية التجارية بتتنفذ مرة واحدة والتاني بيسترجع النتيجة بعد اكتمالها (مطلبك).
3. **ترتيب ثابت للموارد المشتركة** في كل المسارات (Sale، StockReceipt، Refund، أي مسار يلمسهم):
   قفل المفتاح (applock) ثم Shift ثم Products مرتبة بالـ Id تصاعديًا ثم الكتابة (Sale/Receipt + Items + CashTransaction + Audit). المنتجات تتحمّل بـ UPDLOCK مرتبة
   (`ORDER BY Id`) في أول الـ transaction بدل ما تتحمل بشكل غير مرتب (`ProductRepository.GetManyAsync` دلوقتي).
   هدف الترتيب: كل الطلبات تاخد نفس الأقفال بنفس التسلسل، فمفيش cycle.
4. ما نمنعش الـ unique index (يفضل خط الدفاع الأخير ضد التكرار). الـ applock بيقلل التسابق، والـ index بيضمن إن التكرار مستحيل لو الـ lock فشل.

### المسار 2: CashTransactions / Shifts (يعالج C)
1. نعدّل تحميل الـ Shift في مسارات الكتابة: ما نحمّلش كل CashTransactions (Include). نحمّل صف Shift بس بـ UPDLOCK جوه الـ transaction،
   ونضيف CashTransaction الجديدة كـ INSERT. (يلزم مراجعة إن `Shift.RecordCash` محتاجة الـ ledger الكامل ولا ExpectedCash كفاية؛ ده فحص كود قبل التنفيذ.)
2. Shift UPDLOCK في أول الـ transaction بيخلي المبيعات في نفس الـ Shift تتسلسل على صف واحد بدل ما تتسابق على فهرس CashTransactions. الكلفة: توازي أقل
   على نفس الـ Shift (متجر واحد، shift واحد مفتوح عالميًا حسب المخطط، فالكلفة مقبولة بس لازم تتقاس بـ SC04).
3. الفهرس IX_CashTransactions_ShiftId_CreatedAt: مراجعة إن الـ SELECT ما يعملش scan واسع. لو التحميل بقى Shift بس، الفهرس مش في مسار القراءة.
4. بدائل لتقليل تعارض القراءة والكتابة من غير RCSI: (أ) تقصير الـ transaction بإنهاء كل القراءات والحسابات قبل ما نفتح الـ transaction للكتابة (القراءة الحالية مش في transaction أصلًا؛ المشكلة
   إنها بتتعارض مع الكتابة المتزامنة)، (ب) UPDLOCK على صف Shift كما فوق، (ج) تحميل ExpectedCash من العمود بدل جمع الحركات. كلها بتحافظ على RCSI مقفول.
   RCSI نفسه بيحل النوع C تقريبًا لكنه تغيير على القاعدة، ومش جزء من التصميم، ويحتاج تقرير واختبارات وموافقة منفصلة.

## 4) حدود الـ Transaction
- بداية الـ transaction: بعد التحقق من المدخلات (من غير DB)، وقبل أول قراءة محتاجة locks (قفل المفتاح، Shift، Products).
- جوه الـ transaction: إعادة فحص الـ Idempotency، حمل Shift وProducts بالترتيب، بناء الكيان، INSERT/UPDATE، Audit، Commit.
- برّه الـ transaction: أي حاجة مش محتاجة قفل (تحميل Settings، حساب الـ Fingerprint، تحويل DTO للرد).
- رقم الفاتورة من الـ SEQUENCE: بيتاخد جوه الـ transaction بعد الـ locks (مفيش lock، فجوة مسموحة).

## 5) Retry محدود لـ 1205 فقط
- الغلاف حوالين العملية كاملة من حدود الـ transaction (مش SaveChanges بس): `Execute(operation)` بيعيد من أول الـ Handler.
- كل محاولة: DbContext جديد، transaction جديدة، نفس الـ Idempotency Key.
- يلتقط `SqlException.Number == 1205` بس (بما فيه اللي جوه DbUpdateException/InvalidOperationException كما ظهر في الـ Runs). أي خطأ تاني (2601/2627، Concurrency، Business rule) مساره الحالي من غير تغيير.
- عدد المحاولات 3 بحد أقصى مع backoff قصير مع jitter (مثلًا 50-200ms). بعد آخر محاولة يرجع PersistenceConflictException برسالة واضحة والـ InnerException الأصلي محفوظ (الـ retry مش بيخفي السبب).
- تسجيل كل retry: Log + سطر audit/diagnostic بالمفتاح، رقم المحاولة، رقم الخطأ، اسم العملية، وقت الانتظار، من غير بيانات حساسة.
- منع التكرار: الـ rollback الكامل لـ transaction الضحية + إعادة فحص الـ Idempotency في أول كل محاولة + الـ unique index يمنعوا Invoice أو StockMovement أو CashTransaction مكررة.
  أرقام الفواتير المحجوزة في محاولة فاشلة بتبقى فجوات (مسموحة حسب قراراتك) وتتسجل كـ "unused number" بسبب deadlock.

## 6) حالة Commit نجح ثم انقطع الاتصال قبل وصول النتيجة
- لو الاتصال اتقطع بعد الـ commit، العميل أو الـ retry يرسل نفس المفتاح: أول خطوة جوه كل محاولة هي فحص الـ Idempotency، فيلاقي الفاتورة/الاستلام الأصلي ويرجّعه كأنه نجاح، من غير تنفيذ ثاني.
- نفس السلوك لو فشل الاتصال أثناء الـ Commit نفسه (النتيجة غير معروفة): المحاولة التالية تلاقي الصف لو الـ commit تم، أو تنفذ من جديد لو لا. ما نفترضش الفشل ولا النجاح قبل ما نفحص بالمفتاح.
- لازم الـ Retry ما يتفعّلش على أخطاء الاتصال العامة (مش 1205) لأن النتيجة فيها غير معروفة؛ لو اتضافت لاحقًا لازم تعدي من نفس فحص المفتاح.

## 7) ربط كل توصية بالـ graph اللي بيبررها
| التوصية | Graph |
|---|---|
| applock لكل Idempotency Key + إعادة الفحص بعد القفل | النوع A (SC10) والنوع B (SC05) |
| ترتيب ثابت: مفتاح، Shift، Products بالـ Id | A وB (دورة Products ضد الـ unique index) و3 من C (فيها Products) |
| Shift بـ UPDLOCK ومن غير Include لكل CashTransactions | النوع C (SC04): القارئ S ضد الكاتب X/U |
| Retry 1205 بمعاملة وDbContext جدد وإعادة العملية كاملة | كل الأنواع (1205 بيحصل لو الترتيب اتكسر بأي مسار تاني) |
| RCSI | النوع C فقط، ومقفول |

## 8) خطة الاختبار (بعد الموافقة، قبل ما نعتمد التنفيذ)
- Workflow التشخيصي (sqlserver-deadlock-diagnostic.yml) بيتشغّل قبل وبعد: الهدف 0 graph في 30 تكرار (دلوقتي 30 graph في 30 تكرار).
- SC04/SC05/SC10 كلهم لازم ينجحوا في كل التكرارات، ونفس نتائج SC03/SC07/SC08 (فاتورة واحدة، حركة كاش واحدة، خصم مخزون مرة).
- اختبار إضافي: طلبين بنفس المفتاح يرجّع الثاني نفس الـ Invoice/Receipt، وعد الصفوف (Sales, StockReceipts, StockMovements, CashTransactions) = 1.
- اختبار retry: إجبار 1205 مرة واحدة (مثلًا بـ transaction تانية بتقفل Shift) والتأكد من محاولة تانية ناجحة وتسجيلها.
- بعد التنفيذ: مراجعة إن رقم الفاتورة مفيهوش تكرار، والفجوات متسجلة.

## 9) أسئلة للقرار
1. موافق على المسارين (applock + ترتيب ثابت + Shift UPDLOCK) والـ Retry (3 محاولات) كما فوق؟
2. موافق إن Shift UPDLOCK يسلسل المبيعات على نفس الـ Shift (كلفة توازي مقابل إلغاء النوع C)؟
3. تغيير تحميل الـ Shift (من غير Include لكل CashTransactions) محتاج فحص كود قبل التنفيذ: موافق نبدأ به كفحص أولًا؟
