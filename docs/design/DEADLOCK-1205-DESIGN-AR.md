# تقرير تصميم: Deadlock (SQL Server error 1205) في البيع واستلام المخزون
الحالة: تحليل وتصميم فقط. مفيش كود اتغيّر. مستني موافقة المستخدم.

## 1) اللي اتثبت على SQL Server حقيقي (GitHub Actions)
- Run 45638519595: SC04 (20 بيع متزامن) وSC10 (نفس استلام المخزون 6 مرات) فشلوا بـ 1205 عند `ShiftRepository.GetWithCashAsync`
  (قراءة Shift + CashTransactions) من SaleHandler.cs:151 وStockReceiptHandler.cs:107.
- Runs تانية: SC05 (نفس الـ key متزامن) وSC04 فشلوا بـ 1205 تاني، المرة دي جوه `UnitOfWork.SaveChangesAsync` (SaleHandler.cs:214)
  اتغلّف في DbUpdateException. يعني الـ deadlock بيحصل في القراءة وفي الحفظ.
- متقطّع: ~50% من التشغيلات (حوالي 6 من 11 تشغيل ظهر فيها deadlock واحد على الأقل). التشغيلات اللي مفيهاش deadlock كل اختبارات التزامن نجحت.
- النتيجة للمستخدم: العملية بتفشل بخطأ 500 خام (SqlException)، مش PersistenceConflictException، فمفيش رسالة "حاول تاني".
  لم يظهر مبلغ غلط أو فاتورة مكررة في أي تشغيل (SC03/SC05/SC07/SC08 بيفحصوا ده وبينجحوا لما مفيش deadlock).
- مش متاح لحد دلوقتي: deadlock graph. أضفت للـ CI التقاط xml_deadlock_report من system_health (ci-out/deadlock-reports.xml)،
  وفي التشغيلات بعد الإضافة مفيش deadlock حصل فمفيش graph. الرسم لازم يتأكد قبل اعتماد أي حل (الأسباب تحت فرضيات من قراءة الكود).

## 2) تحليل (فرضيات من الكود، مش مثبتة بالـ graph)
مسار البيع (SaleHandler.CheckoutCoreAsync) من غير transaction صريحة:
1. قراءة Shift مع CashTransactions (READ COMMITTED العادي: Shared locks).
2. قراءة المنتجات، (والجلسة لو فيه).
3. أخد رقم الفاتورة من الـ SEQUENCE (مفيش lock).
4. SaveChanges: EF بيفتح transaction ويكتب: INSERT Sale + SaleItems، UPDATE Products (RowVersion)،
   INSERT CashTransaction + UPDATE Shifts (ExpectedCash، RowVersion)، INSERT AuditLogs، وUPDATE Session لو فيه.
مسار استلام المخزون: نفس الشكل (Shift، Products، StockReceipt، CashTransaction).
الفرضيات:
- H1 (قراءة مقابل كتابة): طلب بيقرا Shift+CashTransactions بياخد S locks على الجدولين بالترتيب Shifts ثم CashTransactions،
  وطلب تاني في SaveChanges ماسك X على CashTransactions (INSERT) وبيحتاج UPDATE على Shifts. كل واحد مستني التاني.
- H2 (ترتيب كتابة مختلف): EF بيرتّب الأوامر حسب الاعتماديات، ومسارات البيع والاستلام والاسترداد ممكن تلمس Products وShifts بترتيب مختلف،
  أو تلمس منتجات بترتيب مختلف بين طلبين (طلبين بنفس المنتجات بترتيب مختلف).
- H3 (فهارس): INSERT في CashTransactions/AuditLogs بياخد locks على index pages مع UPDATE على Shifts.
- ضغط الاختبارات (20 طلب فوق shift واحد ومنتج واحد) قاسي أكتر من الواقع، بس ضغطتين متزامنتين عند الكاشير ممكن تعمل نفس النمط.

## 3) خيارات التصميم (بالترتيب المقترح)
A. ترتيب وصول ثابت للموارد: دايمًا Shift ثم Products (مرتبة بالـ Id) ثم Session. نفس الترتيب في Sale وStockReceipt وRefund.
   - المنتجات تتحمّل مرتبة بـ Id (ProductRepository.GetManyAsync دلوقتي من غير ترتيب مضمون).
B. تقصير الـ transaction وتقليل القراءات المتشاركة:
   - قراءة Shift من غير Include على كل CashTransactions لو مش لازم الكل (الاحتياج الحقيقي ExpectedCash).
   - تحديث الـ ExpectedCash والـ cash INSERT في نفس الـ SaveChanges وبأقل عدد جداول.
C. إعداد قاعدة البيانات: READ_COMMITTED_SNAPSHOT ON على قاعدة GHOST (القراءات متاخدش S locks). ده قرار للقاعدة مش للكود،
   بيشيل H1 تقريبًا، لكنه تغيير deployment محتاج موافقة منفصلة واختبار على Express.
D. Retry محدود لـ 1205 فقط:
   - يتلقط SqlException.Number == 1205 (جوه UnitOfWork أو في طبقة تغلّف الـ Handler) بعدد محدود (مثلاً 3) وbackoff صغير.
   - كل محاولة بـ DbContext جديد وtransaction جديدة (الـ context بعد الفشل بيبقى tracked على تغييرات فاشلة، فمينفعش يتعاد استخدامه).
   - نفس IdempotencyKey: لو المحاولة الأولى كانت اتحفظت فعلًا، الـ GetByIdempotencyKey في أول الـ Handler بيرجّع الفاتورة الأصلية.
   - رقم الفاتورة: كل محاولة بتاخد رقم جديد من الـ sequence (فجوة مسموحة حسب قرار 25 نوفمبر: فجوات أيوه، تكرار أبدًا)،
     وتتسجل كـ "unused number" بسبب deadlock.
   - مفيش فاتورة/حركة مخزون/حركة كاش مكررة: لأن الفشل في SaveChanges بيعمل rollback كامل للـ transaction، والـ key الفريد يمنع التكرار.
   - الـ retry ما يخفيش السبب: يتسجل log/audit بـ "Deadlock retried" (عدد المحاولات ورقم المحاولة)، وبعد آخر محاولة يرجع
     PersistenceConflictException برسالة واضحة، وما يبلعش الـ exception الأصلي (InnerException محفوظ).
   - الـ retry لـ 1205 بس. أي خطأ تاني (unique 2601/2627، concurrency) مسارهم الحالي ما يتغيرش.
E. EnableRetryOnFailure في EF: مش مقترح (بيعيد الـ transaction ككل بشكل عام، وبيتعارض مع Handlers اللي بتبدأ transactions يدوية في Serializable وفي مسارات الـ refresh token).

## 4) التوصية
1) التقاط deadlock graph حقيقي أولًا (أضفنا الالتقاط في CI؛ ممكن نشغّل SC04 في loop لحد ما يظهر).
2) تطبيق A (ترتيب ثابت) + D (retry محدود لـ 1205) كحماية. C قرار منفصل للمستخدم.
3) اختبارات: SC04/SC10/SC05 تفضل، ونضيف اختبار يحاكي deadlock مضبوط (طلبين بنفس الموارد بترتيب معكوس) ويتأكد من:
   فاتورة واحدة، حركة كاش واحدة، خصم مخزون مرة، واستخدام المحاولة الثانية.

## 5) الأسئلة المطلوب قرارها
- نمشي في A+D؟ (تعديل على مسار البيع واستلام المخزون)
- C (READ_COMMITTED_SNAPSHOT) نقيّمه ولا لأ؟ (تغيير على القاعدة)
- عدد المحاولات (3؟) والـ backoff.
