# GHOST — SRS + ERD v0.2

نظام إدارة وتشغيل ومحاسبة لمكان PlayStation (10 أجهزة، ورديتين Staff، Admin واحد). Stack: .NET 10 Web API + SQL Server + EF Core + Next.js (Staff + Admin). واجهات الويب تستهلك الـ API، وكل الحسابات والصلاحيات وحالة المخزون والجلسات في الـ Backend.

---

# 1. قرارات العمل المعتمدة (Decisions Log)

| ID | القرار |
| --- | --- |
| BD-01 | الوقت بالثانية، والحساب من ثواني اللعب الفعلية بدون تقريب وقت. |
| BD-02 | مفيش حد أدنى ومفيش دقايق مجانية. العميل يدفع الوقت الفعلي. |
| BD-03 | **نافذة الإلغاء:** أول 5 دقايق من بداية الجلسة (`CancelWindowSeconds = 300`، الأدمن يعدلها) يقدر الستاف يلغي الجلسة بدون أي فلوس. بعدها الإلغاء للأدمن فقط. |
| BD-04 | **التقريب:** لأعلى لأقرب جنيه (Ceiling) مرة واحدة على إجمالي فلوس اللعب للجلسة. 8.33 ← 9، و40.00 ← 40. |
| BD-05 | Multi = سعر ثابت للجهاز كله، من غير عدد لاعبين. |
| BD-06 | الأسعار على مستوى كل جهاز: 10 أجهزة × (Single, Multi) = 20 سعر. |
| BD-07 | 10 أجهزة: Device 1–5 (Regular)، VIP 1–3، Room 1–2. |
| BD-08 | فلوس البضاعة من الدرج، وتتسجل كـ `CashTransaction` خروج. |
| BD-09 | خزنة واحدة ووردية مفتوحة واحدة في نفس الوقت، تسليم نقدي موثق بين الستاف. |
| BD-10 | طابعة فواتير 80mm حرارية. |

## معادلة التسعير

```
BillableSeconds = Σ seconds لكل Segment (فترات الـ Pause مش بتتحسب)
RawCost   = Σ ( segment.Seconds × segment.HourlyRateSnapshot / 3600 )
GamingAmount = Ceiling(RawCost)   // لأعلى جنيه، مرة واحدة
```

ملاحظات تنفيذ مهمة:

- التقريب على **إجمالي الجلسة** مش على كل Segment، عشان العميل ميتظلمش.
- نحسب بأعداد صحيحة (قروش) أو `decimal` مع الضرب قبل القسمة، عشان 40.00 ميتحولش بالغلط لـ 41 بسبب كسور عشرية.
- أي جلسة بعد نافذة الإلغاء أقل تكلفة ليها 1 ج (لأن أي ثانية موجبة تتقرب لـ 1). ده نتيجة طبيعية لقرار التقريب.
- العداد في الشاشة يعرض التكلفة الحالية بنفس القاعدة، فالستاف والعميل يشوفوا رقم واحد.

## قاعدة إلغاء الجلسة (BD-03 تفصيلًا)

1. الشرط: الجلسة `Active` أو `Paused` و `ServerNow − StartedAt ≤ CancelWindowSeconds`.
2. لازم سبب من قائمة: غيّر رأيه، جهاز غلط، نقل مكانه، خطأ في التشغيل، أخرى.
3. النتيجة: `Status = Cancelled`، المبلغ 0، ما يتصدرش فاتورة لعب، ويتسجل `SessionEvent` و`AuditLog`.
4. لو فيه منتجات اتضافت على الجلسة: تتحول لفاتورة منتجات مستقلة أو تتلغي ويرجع مخزونها (Stock Movement عكسي)، والنظام يسأل الستاف.
5. بعد النافذة: الستاف يقدر **ينهي** الجلسة فقط (يدفع الوقت الفعلي). الإلغاء يبقى `VoidSession` للأدمن بسبب مكتوب.
6. الإلغاءات كلها تظهر في تحليلات الستاف (عدد الجلسات الملغية لكل موظف) وفي Alerts الأدمن.

**إضافة مقترحة: نقل الجلسة (Transfer Session).** بدل ما الستاف يلغي ويبدأ من جديد لما العميل ينقل مكانه، يعمل `TransferSession`: النظام يقفل Segment الجهاز القديم بسعره ويفتح Segment على الجهاز الجديد بسعره، بشرط إن الجهاز الجديد متاح. الوقت ما بيضيعش ومفيش جلسة مكررة.

---

# 2. الأدوار والصلاحيات

| الوظيفة | Admin | Staff |
| --- | --- | --- |
| إدارة أسعار اللعب والمنتجات | نعم | لا |
| مشاهدة التكلفة والأرباح والتحليلات | نعم | لا |
| مشاهدة ورديات الآخرين | نعم | لا |
| بدء/إيقاف/استئناف/إنهاء/نقل جلسة | نعم | نعم |
| إلغاء جلسة خلال 5 دقايق | نعم | نعم |
| إلغاء جلسة بعد 5 دقايق (Void) | نعم | لا |
| بيع منتجات وطباعة فاتورة | نعم | نعم |
| تسجيل استلام بضاعة | نعم | نعم (من غير سعر بيع) |
| اعتماد/رفض استلام بضاعة ومنتج جديد | نعم | لا |
| خصم على فاتورة | نعم | لا (أو بموافقة) |
| مصروف / سحب نقدي | نعم | نعم (على ورديته، والسحب بموافقة) |
| جرد مخزون | نعم | نعم (جرد أعمى Blind) |
| مشاهدة فلوس ورديته والمطلوب تسليمه | نعم | نعم (ورديته فقط) |
| إدارة المستخدمين وAudit Log والنسخ الاحتياطي | نعم | لا |

قاعدة: كل Endpoint يتحقق من الدور ومن ملكية المورد (`ShiftId = currentShift`). مجرد إخفاء الأزرار مش كفاية.

---

# 3. تقييم الأفكار الجديدة وقرار الإدراج

| الفكرة | القرار | السبب |
| --- | --- | --- |
| باركود + بحث سريع + إضافة سريعة | **P1 (MVP)** | أساس سرعة الكاشير. Scanner بيعمل كلوحة مفاتيح فالتنفيذ بسيط. |
| تصنيفات منتجات جاهزة | **P1** | مشروبات باردة/ساخنة، عصاير، سناكس. |
| Stock Movement لكل حركة (ممنوع تعديل الكمية مباشرة) | **P1** | أهم قاعدة لسلامة المخزون. |
| سجل أسعار الشراء والبيع | **P1** | عشان هامش الربح والتاريخ. |
| الحد الأدنى لكل منتج + ألوان الحالة | **P1** | القيم الافتراضية: أكثر من 30 ممتاز، 10 إلى 30 منخفض، أقل من 10 حرج، وتتعدل لكل منتج. |
| الجرد الفعلي (Stocktake) | **P1** | مع **جرد أعمى**: الستاف ميشوفش الكمية المسجلة، فيعد بصدق. |
| مرتجعات + خصومات | **P1** | بصلاحية الأدمن. |
| الموردين والمشتريات | **P2** | نبدأ بسيط (اسم المورد على الإيصال)، والدفعات والمتبقي بعدين. |
| الوصفات (Recipe) للعصاير | **P2** | مفيدة جدًا، لكن معقدة. الـ ERD يجهز لها من دلوقتي. |
| العروض (Bundles) | **P2** | مثال: مشروبين + شيبسي. بتتبني فوق نفس بنية المنتج المركب. |
| تنبيهات الصلاحية | **P2** | تاريخ صلاحية اختياري على بند الاستلام. |
| Smart Alerts للأدمن (غير مسبوقة) | **P2** | عجز خزنة، إلغاءات كتير، خصومات، تعديل مخزون سالب. |
| العملاء والولاء والديون | **P3** | خطر على الخزنة: البيع الآجل مش نقدية. ما نبنيهوش إلا لو فيه احتياج حقيقي. الـ ERD يسيب `CustomerId` فاضي. |
| طرق الدفع الإلكترونية | **P3** | `PaymentMethod` جاهز للتوسع، النقدي فقط دلوقتي. |
| قوالب فواتير متعددة | **مؤجل** | 80mm واحد يكفي. |

القرار: نبدأ بالجلسات والورديات والمنتجات والباركود وPOS، ثم الباقي بالترتيب.

---

# 4. SRS — المتطلبات الوظيفية

## 4.1 الدخول والصلاحيات

- **FR-01** (P1): تسجيل دخول بـ Username/Password (Hashing)، JWT قصير مع Refresh.
- **FR-02** (P1): Admin ينشئ ويعطل حسابات الستاف.

## 4.2 الأيام والورديات والخزنة

- **FR-10** (P1): فتح يوم عمل واحد فقط.
- **FR-11** (P1): فتح وردية برصيد افتتاح؛ وردية واحدة مفتوحة فقط (Unique Filtered Index).
- **FR-12** (P1): تسليم الوردية: الستاف يدخل `ActualCash`، النظام يحسب `Expected` والفرق، والستاف التالي يؤكد الاستلام أو يسجل اختلاف.
- **FR-13** (P1): منع قفل الوردية لو فيه فاتورة مفتوحة أو جلسة معلقة، إلا باستثناء من الأدمن بسبب مكتوب.
- **FR-14** (P1): مصروفات وسحوبات نقدية على الوردية. السحب ليس مصروفًا تشغيليًا ويتسجل بنوع وسبب ومستلم وموافق.
- **FR-15** (P1): الستاف يشوف فلوس ورديته والمطلوب تسليمه فقط.

```
ExpectedCash = OpeningCash + CashSales + CashIn − Expenses − Withdrawals − StockPurchases − CashRefunds ± HandoverMoves
Difference   = ActualCash − ExpectedCash
```

## 4.3 الأجهزة والجلسات

- **FR-20** (P1): 10 أجهزة بحالات Available/Occupied/Paused/Maintenance/Disabled.
- **FR-21** (P1): بدء جلسة (Single/Multi) على جهاز متاح؛ جلسة نشطة واحدة لكل جهاز (Unique Filtered Index).
- **FR-22** (P1): Pause/Resume/End، والبدء والنهاية بتوقيت الـ Server.
- **FR-23** (P1): تجميد السعر في كل Segment وقت فتحه. تغيير الأدمن للسعر لا يؤثر على الجلسات الجارية.
- **FR-24** (P1): تغيير Single/Multi أثناء الجلسة يقفل Segment ويفتح غيره.
- **FR-25** (P1): إلغاء خلال نافذة الـ 5 دقايق حسب BD-03.
- **FR-26** (P1): نقل الجلسة بين الأجهزة.
- **FR-27** (P1): استرجاع الجلسات بعد انقطاع الكهرباء أو غلق البرنامج، لأن كل حاجة محفوظة في الـ DB.
- **FR-28** (P1): Timeline للجلسة بكل أحداثها.

## 4.4 المنتجات والمخزون

- **FR-30** (P1): منتج بـ Barcode فريد، وSKU، وصورة، وتصنيف، وحد أدنى. الأدمن فقط يغير السعر.
- **FR-31** (P1): مسح الباركود ينتج: منتج موجود ← إضافة للفاتورة فورًا؛ غير موجود ← نموذج سريع.
- **FR-32** (P1): توليد باركود داخلي وطباعة ملصقات للمنتجات اللي ملهاش باركود.
- **FR-33** (P1): كل تغير كمية = `StockMovement` (Append-only).
- **FR-34** (P1): استلام بضاعة: الستاف يسجل المنتج، الكمية، سعر الشراء. النظام يسجل خروج نقدية من الدرج. المخزون يزيد فورًا بحالة Pending، والأدمن يوافق أو يرفض.
- **FR-35** (P1): لو الأدمن رفض: حركة عكسية للمخزون والنقدية، أو تحويل المبلغ لعجز على الستاف، مع سبب.
- **FR-36** (P1): منتج جديد من الستاف = `PendingPricing`، مينفعش يتباع قبل اعتماد الأدمن للسعر.
- **FR-37** (P1): منع بيع كمية أكبر من المتاح (وقفل تزامن).
- **FR-38** (P1): جرد فعلي (Blind للستاف) يظهر العجز والزيادة، وتسوية تحتاج اعتماد الأدمن.
- **FR-39** (P1): تلف، انتهاء صلاحية، استهلاك: كلها حركات بسبب إجباري.
- **FR-40** (P2): Recipes: بيع عصير يخصم مكوناته.
- **FR-41** (P2): Bundles.
- **FR-42** (P2): موردون وفواتير شراء ودفعات.
- **FR-43** (P2): تنبيهات الصلاحية واقتراح إعادة الطلب.
- **FR-44** (P1): البيع بالقطعة أو العبوة (`ProductUnit` بباركود وعامل تحويل).

## 4.5 المبيعات والفواتير

- **FR-50** (P1): POS سريع: مسح/بحث، تعديل الكمية، إجمالي تلقائي.
- **FR-51** (P1): فاتورة واحدة تجمع بند لعب وبنود منتجات.
- **FR-52** (P1): رقم فاتورة فريد (`GS-2026-00001`) من SQL Sequence.
- **FR-53** (P1): تخزين سعر البيع والتكلفة وقت البيع داخل `SaleItem`.
- **FR-54** (P1): دفع نقدي مع Idempotency Key.
- **FR-55** (P1): خصم (Admin)، إلغاء/استرداد (Admin) بدون حذف فواتير نهائية.
- **FR-56** (P1): طباعة 80mm، وإعادة الطباعة تعرض "نسخة" وتتسجل في `PrintJobLog`.
- **FR-57** (P1): خصم المخزون والفاتورة في Transaction واحدة.

## 4.6 التحليلات والتقارير (Admin)

- **FR-60** (P1): Dashboard: الإيراد، إيراد اللعب، إيراد المنتجات، ساعات اللعب، المصروفات، الأجهزة.
- **FR-61** (P1): فلاتر: من/إلى، أيام محددة، موظف، جهاز، نوع لعب، تصنيف منتج، حالة فاتورة، وردية، ومقارنة بفترة سابقة.
- **FR-62** (P1): أعلى وأقل يوم، أفضل أيام الأسبوع، Heatmap الساعات.
- **FR-63** (P1): تحليل المنتجات: الأكثر والأقل مبيعًا، الأرباح، الراكد، منخفض المخزون.
- **FR-64** (P1): تحليل الستاف: ورديات، ساعات، إيراد لكل ساعة، إلغاءات، خصومات، عجز، سحوبات، استلام بضاعة.
- **FR-65** (P1): Activity Feed: "Staff A، وردية 18، 14:35، استلم 12 مانجو بـ 300 ج".
- **FR-66** (P1): Drill-down من أي رقم إلى الفواتير المكونة له.
- **FR-67** (P1): لوحة المخزون: عدد المنتجات، قيمة المخزون بالتكلفة، المنخفض، الراكد، المشتريات، الهامش.
- **FR-68** (P2): Smart Alerts: عجز خزنة فوق حد، إلغاءات متكررة، خصومات غير عادية، تسويات مخزون سالبة.

## 4.6.1 الطباعة والتصدير

- **FR-70** (P1): PDF + Print لكل تقرير: يومي، أسبوعي، شهري، وردية، سحوبات الستاف (بفلتر موظف وفترة)، مصروفات شهرية مقسمة بالفئة، مبيعات المنتجات، حركة المخزون، جلسات اللعب، فروقات الخزنة، Audit.
- **FR-71** (P1): Excel/CSV للتقارير.
- **FR-72** (P1): نفس الأرقام في الشاشة والـ PDF لأن الاثنين من نفس الـ Query.

## 4.7 النظام

- **FR-80** (P1): Audit Log لكل عملية حساسة (سعر، خصم، إلغاء، تسوية، دخول).
- **FR-81** (P1): نسخ احتياطي عند الإغلاق والتشغيل وتلقائي يوميًا، مع الاحتفاظ بآخر 7 نسخ، واختبار استعادة.

---

# 5. المتطلبات غير الوظيفية

- **Consistency:** الفواتير والمخزون والخزنة متطابقين. العمليات المالية في Transactions.
- **Concurrency:** `RowVersion` على Device وProduct وStaffShift وGamingSession.
- **Idempotency:** على الدفع وإنهاء الجلسة والإلغاء.
- **Time:** كل الأوقات UTC من الـ Server، والعرض بتوقيت مصر.
- **Money:** `decimal(18,2)`، ممنوع `float/double`.
- **Performance:** Pagination، Indexes على التواريخ، `DailySummary` و`ShiftSummary` للتحليلات، و`AsNoTracking`.
- **Security:** صلاحيات في الـ Backend، Audit غير قابل للتعديل.
- **Recoverability:** النسخ الاحتياطي واسترجاع الجلسات.
- **Testing:** xUnit + Integration Tests على SQL Server حقيقي.

---

# 6. ERD v0.2

الرموز: PK مفتاح أساسي، FK مفتاح أجنبي، UQ فريد، RV = RowVersion، (P2)/(P3) للمراحل المؤجلة.

## 6.1 Identity

| Table | أهم الأعمدة |
| --- | --- |
| `Users` | Id PK، Username UQ، PasswordHash، FullName، IsActive، CreatedAt |
| `Roles` | Id PK، Name (Admin/Staff) |
| `UserRoles` | UserId FK، RoleId FK |
| `RefreshTokens` | Id، UserId FK، TokenHash، ExpiresAt، RevokedAt |

## 6.2 التشغيل والخزنة

| Table | أهم الأعمدة | قيود |
| --- | --- | --- |
| `BusinessDays` | Id، Date، OpenedById، OpenedAt، ClosedAt، Status | يوم مفتوح واحد |
| `StaffShifts` | Id، BusinessDayId FK، UserId FK، StartedAt، EndedAt، OpeningCash، ExpectedCash، ActualCash، Difference، Status، RV | **وردية Open واحدة** (Filtered UQ) |
| `ShiftHandovers` | Id، FromShiftId FK، ToShiftId FK، HandedCash، ReceivedCash، Difference، AcknowledgedAt، Note |  |
| `CashTransactions` | Id، ShiftId FK، Type، Direction (In/Out)، Amount، SourceType، SourceId، Reason، CreatedById، CreatedAt | لا تعديل ولا حذف |
| `Withdrawals` | Id، CashTransactionId FK، BeneficiaryUserId FK، Reason، ApprovedById FK |  |

`CashTransactions.Type`: OpeningBalance، SaleCash، Expense، Withdrawal، StockPurchase، StockPurchaseReversal، HandoverOut، HandoverIn، Refund، Adjustment، (P3) DebtCollection.

## 6.3 الأجهزة والجلسات

| Table | أهم الأعمدة | قيود |
| --- | --- | --- |
| `DeviceCategories` | Id، Name (Regular/VIP/Room) |  |
| `Devices` | Id، Name، CategoryId FK، Status، SortOrder، RV | Name UQ |
| `DeviceRates` | Id، DeviceId FK، Mode (Single/Multi)، HourlyRate، EffectiveFrom، EffectiveTo، CreatedByAdminId، Reason | سعر حالي واحد لكل (Device, Mode) |
| `GamingSessions` | Id، DeviceId FK، StartedById، StartedShiftId، EndedById، EndedShiftId، StartedAt، EndedAt، Status (Active/Paused/Ended/Cancelled)، BillableSeconds، GamingAmount، CancelReason، CancelledAt، IdempotencyKey UQ، RV | **جلسة Active/Paused واحدة لكل جهاز** (Filtered UQ) |
| `SessionSegments` | Id، SessionId FK، SegmentNo، DeviceId FK، Mode، HourlyRateSnapshot، StartedAt، EndedAt، Seconds |  |
| `SessionEvents` | Id، SessionId FK، Type، At، UserId، ShiftId، DataJson | Type: Started، Paused، Resumed، ModeChanged، Transferred، ProductAdded، ProductRemoved، Ended، Cancelled، Voided |

`DeviceRates` المنتهية (`EffectiveTo` غير فاضي) هي تاريخ الأسعار، فمفيش جدول History منفصل.

## 6.3.1 قاعدة الإلغاء في الداتا

الإلغاء = تغيير `Status` إلى Cancelled مع `CancelReason` و`BillableSeconds` محفوظة للتحليل، و`GamingAmount = 0`. الجلسة الملغية تظل موجودة للتقارير ولا تُحذف.

## 6.4 المنتجات والمخزون

| Table | أهم الأعمدة | قيود |
| --- | --- | --- |
| `ProductCategories` | Id، Name، ParentId، SortOrder |  |
| `Products` | Id، Name، Barcode، Sku، CategoryId FK، ProductType (Simple/Recipe/Bundle)، SalePrice، AvgCost، MinStock، WarningStock، TrackStock، HasExpiry، ImagePath، Status (Active/PendingPricing/Disabled)، RV | **Barcode UQ (Filtered غير Null)** |
| `ProductUnits` | Id، ProductId FK، UnitName (قطعة/عبوة)، Barcode UQ، FactorToBase، SalePrice | بيع بالقطعة أو العبوة |
| `ProductPriceHistory` | Id، ProductId FK، PriceType (Sale/Cost)، OldValue، NewValue، ChangedById، ChangedAt، Reason |  |
| `Recipes` (P2) | Id، ProductId FK |  |
| `RecipeItems` (P2) | RecipeId FK، IngredientProductId FK، Quantity |  |
| `BundleItems` (P2) | BundleProductId FK، ComponentProductId FK، Quantity |  |
| `StockMovements` | Id، ProductId FK، Type، QtyDelta، UnitCost، BalanceAfter، ShiftId FK، UserId FK، SourceType، SourceId، Reason، CreatedAt | Append-only، `Reason` إجباري |
| `StockBatches` (P2) | Id، ProductId FK، ExpiryDate، QtyRemaining |  |
| `StockReceipts` | Id، ShiftId FK، CreatedById، SupplierId FK (P2)، Status (Pending/Approved/Rejected)، TotalCost، ApprovedById، ApprovedAt، RejectionReason |  |
| `StockReceiptItems` | Id، ReceiptId FK، ProductId FK، Quantity، UnitCost، ExpiryDate |  |
| `Stocktakes` | Id، StartedById، ShiftId FK، Status، IsBlind، CreatedAt |  |
| `StocktakeItems` | StocktakeId FK، ProductId FK، SystemQty، CountedQty، Difference |  |
| `Suppliers` (P2) | Id، Name، Phone، Notes |  |
| `SupplierPayments` (P2) | Id، SupplierId FK، ReceiptId FK، Amount، CashTransactionId FK |  |

`StockMovements.Type`: Purchase، Sale، SaleReturn، Waste، Expired، RecipeConsumption، StocktakeAdjustment، ManualAdjustment، PurchaseReversal.

## 6.5 المبيعات

| Table | أهم الأعمدة | قيود |
| --- | --- | --- |
| `Sales` | Id، InvoiceNumber UQ، ShiftId FK، CreatedById، SessionId FK (Null)، CustomerId FK (Null، P3)، Status (Draft/Paid/Voided/Refunded)، Subtotal، DiscountType، DiscountValue، DiscountReason، DiscountApprovedById، Total، PaidTotal، PrintCount، IdempotencyKey UQ، CreatedAt، PaidAt | لا حذف بعد Paid |
| `SaleItems` | Id، SaleId FK، ItemType (Product/Gaming)، ProductId FK (Null)، GamingSessionId FK (Null)، DescriptionSnapshot، Quantity، UnitPrice، UnitCostSnapshot، LineTotal | سعر وتكلفة مجمدة |
| `Payments` | Id، SaleId FK، ShiftId FK، Method، Amount، ReceivedById، PaidAt، IdempotencyKey UQ |  |
| `Refunds` | Id، SaleId FK، ShiftId FK، Amount، Reason، ApprovedById، CreatedAt |  |
| `RefundItems` | RefundId FK، SaleItemId FK، Quantity، ReturnToStock |  |
| `PrintJobLog` | Id، SaleId FK أو ReportType، UserId، PrintedAt، IsCopy |  |

## 6.6 المصروفات والنظام

| Table | أهم الأعمدة |
| --- | --- |
| `ExpenseCategories` | Id، Name |
| `Expenses` | Id، ShiftId FK، CategoryId FK، Amount، Reason، CashTransactionId FK، CreatedById، CreatedAt |
| `AuditLogs` | Id، UserId، Action، EntityType، EntityId، OldValues، NewValues، At |
| `SystemSettings` | Key PK، Value (مثل `CancelWindowSeconds`) |
| `BackupRecords` | Id، Path، Type، CreatedAt، VerifiedAt |
| `DailySummaries` | Date PK، GamingRevenue، ProductRevenue، Expenses، Seconds، Sessions، CancelledSessions، … |
| `ShiftSummaries` | ShiftId PK، المبيعات، الفرق، الإلغاءات، الخصومات، الساعات |
| `Customers` / `CustomerLedger` (P3) | ديون وتحصيلات منفصلة عن المبيعات |

## 6.7 العلاقات الأساسية

- `User 1—N StaffShift`، `BusinessDay 1—N StaffShift`
- `StaffShift 1—N CashTransaction`، `Sale`، `Expense`، `StockReceipt`، `Payment`
- `StaffShift 1—1 ShiftHandover (من)`، و`1—1 ShiftHandover (إلى)`
- `Device 1—N DeviceRate`، `Device 1—N GamingSession`
- `GamingSession 1—N SessionSegment`، `1—N SessionEvent`، `1—0..1 SaleItem`
- `Sale 1—N SaleItem`، `1—N Payment`، `1—N Refund`
- `Product 1—N StockMovement`، `StockReceiptItem`، `SaleItem`، `ProductPriceHistory`
- `StockReceipt 1—N StockReceiptItem`، و`1—1 CashTransaction (StockPurchase)`
- `Stocktake 1—N StocktakeItem`
- `AuditLog` يربط أي كيان بالمستخدم والوقت.

## 6.8 القيود (Constraints) الإلزامية

1. `StaffShifts`: Unique Filtered Index عند `Status = 'Open'`.
2. `GamingSessions`: Unique Filtered Index على `DeviceId` عند `Status IN ('Active','Paused')`.
3. `BusinessDays`: يوم Open واحد.
4. `Products.Barcode`، `ProductUnits.Barcode`، `Sales.InvoiceNumber`: Unique.
5. `DeviceRates`: سعر حالي واحد لكل (Device, Mode).
6. `CHECK`: `Amount >= 0`، `Quantity > 0`، `BillableSeconds >= 0`.
7. `StockMovements`، `CashTransactions`، `AuditLogs`: ممنوع UPDATE/DELETE (بصلاحيات DB أو Triggers).
8. `Payments.IdempotencyKey` و`GamingSessions.IdempotencyKey`: Unique.
9. رصيد المنتج لا ينزل تحت صفر بدون سماح صريح.

---

# 7. قواعد مالية مختصرة

- تسليم الوردية: خروج من عهدة الأولى (`HandoverOut`) ودخول للتانية (`HandoverIn`). ليس إيرادًا ولا مصروفًا.
- جلسة بدأت في وردية وانتهت في تانية: الإيراد يتسجل مرة واحدة، والتحصيل على وردية من استلم الفلوس، والنظام يسجل من بدأ ومن أنهى.
- مرتجع منتج: حركة مخزون `SaleReturn` وخروج نقدي `Refund`.
- السحب النقدي ليس مصروفًا تلقائيًا، وبيتحسب مرة واحدة في التقارير.
- الإيراد غير صافي الربح. الربح يظهر فقط لما التكلفة والمصروفات متوفرة.

---

# 8. خطة المراحل

| المرحلة | المحتوى |
| --- | --- |
| 0 | اعتماد هذا المستند (SRS + ERD) |
| 1 | مراجعة GHOST-2.0 مقابل الـ ERD |
| 2 | Auth + Shifts + Handover + Cash |
| 3 | Devices + Rates + Sessions (Segments + Cancel + Transfer) |
| 4 | Products + Barcode + Inventory + Stocktake + StockReceipt |
| 5 | POS + Invoices + Payments + Refunds + طباعة |
| 6 | Expenses + Withdrawals + Summaries |
| 7 | Analytics + PDF/Excel |
| 8 | Audit + Backup + Smart Alerts |
| 9 | واجهات Next.js (Staff + Admin) وربطها بالـ API |
| 10 | اختبارات ضغط وانقطاع كهرباء واستعادة، ثم تشغيل تجريبي |
| لاحقًا (P2/P3) | Recipes، Bundles، Suppliers، Customers |

---

# 9. أسئلة مفتوحة

1. **تقريب فاتورة المنتجات:** التقريب لأعلى جنيه مطبق على اللعب فقط. هل أسعار المنتجات كلها أرقام صحيحة، أم ممكن يكون فيها نص جنيه؟
2. **الستاف بيخصم؟** المقترح: لا، الأدمن فقط.
3. **سحب الستاف:** هل بموافقة الأدمن قبلها، أم يتسجل ويراجعه الأدمن بعدين؟
4. **استلام البضاعة المرفوض:** افتراضي الأدمن يختار بين عكس المبلغ أو تحميله كعجز على الستاف.
5. **الوصفات:** عايز تبدأ بيها بدري (P1.5) ولا نأجلها لما الأساسيات تشتغل؟
6. **بيانات GHOST-2.0:** ابعت لي هيكل الـ Entities الحالي لمراجعته على الـ ERD.

---

# 10. الكاميرات والوصول عن بُعد (إضافة v0.3)

| المتطلب | القرار |
| --- | --- |
| BD-11 الكاميرات | نظام منفصل: كاميرات IP + NVR + تطبيق الشركة الرسمي. لا دمج مباشر في GHOST v1. |
| BD-12 الوصول عن بُعد | Tailscale VPN للـ API فقط. SQL Server لا يُفتح لأي شبكة. |
| FR-90 (P2) | Web Dashboard للأدمن قراءة فقط: الإيرادات، الجلسات النشطة، حالة الأجهزة، الورديات، التنبيهات، مع وقت آخر تحديث. |
| FR-91 (P1) | تسجيل كل دخول عن بُعد في AuditLog. |
| NFR | السيرفر شغال 24 ساعة ومعاه UPS إلزامي. التشغيل المحلي يستمر لو الإنترنت قطع. |
| FR-81 معدّل | نسخة احتياطية مشفرة يوميًا خارج المكان بالإضافة للنسخ المحلية (آخر 7 نسخ)، واختبار استعادة دوري. |
| تزامن الوقت | NVR وسيرفر GHOST على نفس NTP، عشان أي حدث في GHOST (إلغاء، عجز، Void) يتراجع على الفيديو في نفس الدقيقة. |

**الكاميرات المقترحة:** NVR بـ 8 قنوات و6 إلى 7 كاميرات: الباب، الكاشير والدرج، الصالة (2)، ممر VIP والغرف (1 أو 2)، المخزن. تجنب تصوير اللاعبين داخل الغرف نفسها. التخزين التقريبي 4 إلى 6 تيرا لشهر كامل (يتأكد بحاسبة الشركة).

---

# 11. قرارات جديدة (v0.4)

| ID | القرار |
| --- | --- |
| BD-13 | أسعار بيع المنتجات أرقام صحيحة (جنيه كامل). تكلفة الشراء ممكن تكون بكسور. كل فاتورة إجماليها رقم صحيح لأن اللعب بيتقرب لأعلى والمنتجات أرقام صحيحة. قيد في الـ DB: `SalePrice = FLOOR(SalePrice)`. |
| BD-14 | الستاف يقدر يسحب فلوس من الدرج في أي وقت **بدون موافقة مسبقة**، بشرط يختار تصنيف السبب ويكتب التفاصيل. الأدمن يشوف كل حاجة ويوصله تنبيه. |

## شاشة "سحب من الدرج" (FR-14 معدّل)

الستاف يختار التصنيف ويكتب السبب (إجباري)، والنظام يوجّهه للسجل الصح عشان مفيش حاجة تتحسب مرتين:

| التصنيف | يتسجل في | يظهر في تقرير |
| --- | --- | --- |
| شراء منتجات | `StockReceipt` (المنتج والكمية والتكلفة) ثم `CashTransaction(StockPurchase)` | المشتريات والمخزون |
| سلفة | `Withdrawal(Advance)` والمستفيد موظف | سحوبات الستاف |
| مصروف تشغيلي (نظافة، صيانة) | `Expense` | المصروفات |
| أخرى | `Withdrawal(Other)` + نص إجباري | سحوبات الستاف (متعلّم) |

- كل حاجة بتتسجل فورًا بـ: مين، أنهي وردية، الوقت بالثانية، التصنيف، السبب، المبلغ.
- الأدمن يشوفها في Activity Feed وتقرير سحوبات الستاف. بيظهر تنبيه لو التصنيف "أخرى" أو المبلغ فوق حد معين (`WithdrawalAlertThreshold`) أو سحوبات كتير في وردية واحدة. الأدمن يقدر يراجع ويعلق ويعيد التصنيف (مسجل في Audit).
- شراء المنتجات ما بيتسجلش سحب وإيصال مع بعض، بيتسجل إيصال فقط.
- خصم النسبة المئوية: الأدمن فقط، ويتقرب لأسفل لجنيه كامل عشان الإجمالي يفضل رقم صحيح (افتراض محتاج تأكيدك).

## أسئلة مفتوحة متبقية

1. هل الخصم بالنسبة يتقرب لأسفل (في صالح العميل أو المكان؟ المقترح: لأسفل يعني الخصم أقل).
2. قيمة حد التنبيه للسحب.
3. هل السلفة تتربط بخصم من المرتب لاحقًا (P2)؟
4. رقم عدد الكاميرات وموديل الطابعة وهيكل GHOST-2.0.
