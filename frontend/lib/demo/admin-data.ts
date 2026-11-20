export type Source = 'live' | 'demo' | 'unavailable'
export type DemoRow = { id: string; date: string; name: string; detail: string; status: string; amount: number; durationMinutes?: number; stockQuantity?: number }

export const demoSessions: DemoRow[] = [
  { id: 'DEMO-SES-001', date: '2026-11-20', name: 'أحمد محمد', detail: 'PS5 · Single · ج.م 120 / ساعة', status: 'مكتملة', amount: 240, durationMinutes: 120 },
  { id: 'DEMO-SES-002', date: '2026-11-20', name: 'محمود عادل', detail: 'VIP 1 · Multi · ج.م 85 / ساعة', status: 'جارية', amount: 170, durationMinutes: 120 },
  { id: 'DEMO-SES-003', date: '2026-11-19', name: 'يوسف خالد', detail: 'PS4 · Single · ج.م 75 / ساعة', status: 'مكتملة', amount: 150, durationMinutes: 120 },
  { id: 'DEMO-SES-004', date: '2026-11-19', name: 'عمر حسن', detail: 'Room 1 · Multi · ج.م 140 / ساعة', status: 'ملغاة', amount: 0, durationMinutes: 45 },
  { id: 'DEMO-SES-005', date: '2026-11-18', name: 'كريم سامي', detail: 'PS5 · Single · ج.م 120 / ساعة', status: 'مكتملة', amount: 360, durationMinutes: 180 },
  { id: 'DEMO-SES-006', date: '2026-11-18', name: 'إبراهيم علي', detail: 'VIP 2 · Single · ج.م 110 / ساعة', status: 'مكتملة', amount: 220, durationMinutes: 120 },
]

export const demoProducts: DemoRow[] = [
  { id: 'DEMO-PRD-001', date: '2026-11-20', name: 'مشروب غازي', detail: 'مشروبات · الرصيد 48 قطعة', status: 'متوفر', amount: 35, stockQuantity: 48 },
  { id: 'DEMO-PRD-002', date: '2026-11-20', name: 'مياه معدنية', detail: 'مشروبات · الرصيد 8 قطع', status: 'منخفض', amount: 15, stockQuantity: 8 },
  { id: 'DEMO-PRD-003', date: '2026-11-19', name: 'رقائق بطاطس', detail: 'وجبات خفيفة · الرصيد 31 قطعة', status: 'متوفر', amount: 30, stockQuantity: 31 },
  { id: 'DEMO-PRD-004', date: '2026-11-19', name: 'فشار', detail: 'وجبات خفيفة · الرصيد 22 قطعة', status: 'متوفر', amount: 40, stockQuantity: 22 },
  { id: 'DEMO-PRD-005', date: '2026-11-18', name: 'شوكولاتة', detail: 'حلويات · الرصيد 6 قطع', status: 'منخفض', amount: 25, stockQuantity: 6 },
]

export const demoInventory: DemoRow[] = [
  { id: 'DEMO-MOV-001', date: '2026-11-20', name: 'توريد مياه معدنية', detail: '+24 قطعة · الرصيد بعد الحركة 32', status: 'توريد', amount: 360 },
  { id: 'DEMO-MOV-002', date: '2026-11-20', name: 'صرف مشروبات غازية', detail: '−8 قطع · الرصيد بعد الحركة 48', status: 'صرف', amount: 280 },
  { id: 'DEMO-MOV-003', date: '2026-11-19', name: 'توريد رقائق بطاطس', detail: '+18 قطعة · الرصيد بعد الحركة 31', status: 'توريد', amount: 420 },
  { id: 'DEMO-MOV-004', date: '2026-11-18', name: 'صرف شوكولاتة', detail: '−5 قطع · الرصيد بعد الحركة 6', status: 'صرف', amount: 125 },
]

export const demoEmployees: DemoRow[] = [
  { id: 'DEMO-EMP-001', date: '2026-11-20', name: 'سارة محمود', detail: 'مشرفة صالة', status: 'نشط', amount: 0 },
  { id: 'DEMO-EMP-002', date: '2026-11-20', name: 'علي حسن', detail: 'مشغل أجهزة', status: 'نشط', amount: 0 },
  { id: 'DEMO-EMP-003', date: '2026-11-19', name: 'مريم أحمد', detail: 'خدمة عملاء', status: 'إجازة', amount: 0 },
  { id: 'DEMO-EMP-004', date: '2026-11-18', name: 'حسام فؤاد', detail: 'مشغل أجهزة', status: 'نشط', amount: 0 },
]

export const demoShifts: DemoRow[] = [
  { id: 'DEMO-SHF-001', date: '2026-11-20', name: 'وردية صباحية', detail: '8 ساعات · سارة محمود', status: 'مغلقة', amount: 1850 },
  { id: 'DEMO-SHF-002', date: '2026-11-19', name: 'وردية مسائية', detail: '9 ساعات · علي حسن', status: 'مغلقة', amount: 2340 },
  { id: 'DEMO-SHF-003', date: '2026-11-19', name: 'وردية صباحية', detail: '8 ساعات · مريم أحمد', status: 'قيد المراجعة', amount: 1620 },
  { id: 'DEMO-SHF-004', date: '2026-11-18', name: 'وردية مسائية', detail: '7 ساعات · حسام فؤاد', status: 'مغلقة', amount: 2100 },
]

export const demoSales: DemoRow[] = [
  { id: 'DEMO-INV-1042', date: '2026-11-20', name: 'فاتورة #1042', detail: 'جلسات ج.م 180 · منتجات ج.م 40', status: 'مكتملة', amount: 220 },
  { id: 'DEMO-INV-1041', date: '2026-11-20', name: 'فاتورة #1041', detail: 'جلسة PS5 · منتج واحد', status: 'مكتملة', amount: 95 },
  { id: 'DEMO-INV-1040', date: '2026-11-19', name: 'فاتورة #1040', detail: 'استرداد توضيحي · منتج واحد', status: 'مستردة', amount: -30 },
  { id: 'DEMO-INV-1039', date: '2026-11-19', name: 'فاتورة #1039', detail: 'جلسات ج.م 90 · منتجات ج.م 40', status: 'مكتملة', amount: 130 },
]

export const demoAudit: DemoRow[] = [
  { id: 'DEMO-AUD-001', date: '2026-11-20', name: 'تحديث بيانات جهاز', detail: 'تغيير اسم العرض · حدث توضيحي', status: 'معلومات', amount: 0 },
  { id: 'DEMO-AUD-002', date: '2026-11-20', name: 'تسجيل دخول', detail: 'جلسة عرض تجريبية · سجل توضيحي', status: 'نجاح', amount: 0 },
  { id: 'DEMO-AUD-003', date: '2026-11-19', name: 'تغيير سعر الساعة', detail: 'تعديل توضيحي · لا يغيّر بيانات API', status: 'معلومات', amount: 0 },
  { id: 'DEMO-AUD-004', date: '2026-11-18', name: 'تصدير تقرير', detail: 'تصدير ملف توضيحي', status: 'نجاح', amount: 0 },
]

export const demoUsers: DemoRow[] = [
  { id: 'DEMO-USR-001', date: '2026-11-20', name: 'مستخدم إداري', detail: 'إدارة كاملة', status: 'مدير', amount: 0 },
  { id: 'DEMO-USR-002', date: '2026-11-20', name: 'مشرف النظام', detail: 'إدارة الأجهزة', status: 'مشرف', amount: 0 },
  { id: 'DEMO-USR-003', date: '2026-11-19', name: 'مسؤول التقارير', detail: 'عرض التقارير', status: 'محلل', amount: 0 },
]

export const demoReconciliations: DemoRow[] = [
  { id: 'DEMO-CASH-001', date: '2026-11-20', name: 'وردية صباحية', detail: 'المتوقع 8,450 EGP · المعدود 8,400 EGP', status: 'مراجعة', amount: -50 },
  { id: 'DEMO-CASH-002', date: '2026-11-19', name: 'وردية مسائية', detail: 'المتوقع 6,250 EGP · المعدود 6,250 EGP', status: 'مكتملة', amount: 0 },
  { id: 'DEMO-CASH-003', date: '2026-11-18', name: 'وردية صباحية', detail: 'المتوقع 4,800 EGP · المعدود 4,950 EGP', status: 'مكتملة', amount: 150 },
  { id: 'DEMO-CASH-004', date: '2026-11-17', name: 'وردية مسائية', detail: 'المتوقع 5,100 EGP · المعدود 5,000 EGP', status: 'مراجعة', amount: -100 },
]

export const demoAnalytics = [
  { day: 'السبت', revenue: 1850, hours: 42, sessions: 23, utilization: 42 },
  { day: 'الأحد', revenue: 2200, hours: 51, sessions: 28, utilization: 51 },
  { day: 'الإثنين', revenue: 1740, hours: 39, sessions: 21, utilization: 39 },
  { day: 'الثلاثاء', revenue: 2600, hours: 58, sessions: 32, utilization: 58 },
  { day: 'الأربعاء', revenue: 2380, hours: 54, sessions: 30, utilization: 54 },
  { day: 'الخميس', revenue: 3100, hours: 67, sessions: 38, utilization: 67 },
  { day: 'الجمعة', revenue: 2850, hours: 61, sessions: 35, utilization: 61 },
]
