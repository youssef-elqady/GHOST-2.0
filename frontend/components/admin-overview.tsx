import {
  Activity,
  CalendarDays,
  ChartNoAxesCombined,
  CircleDollarSign,
  Clock3,
  CupSoda,
  Gamepad2,
  Package,
  ShieldCheck,
  Wallet,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import {
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { PageHeader } from '@/components/ui/page-header'

type MetricTone = 'gold' | 'blue' | 'green' | 'violet' | 'neutral'

type MetricCardProps = {
  label: string
  icon: LucideIcon
  tone: MetricTone
  unit: string
  note: string
}

function MetricCard({ label, icon: Icon, tone, unit, note }: MetricCardProps) {
  return (
    <Card className={`admin-metric-card admin-metric-card--${tone}`}>
      <div className="admin-metric-card__top">
        <span className="admin-metric-card__icon"><Icon size={19} aria-hidden="true" /></span>
        <Badge tone="neutral">بانتظار البيانات</Badge>
      </div>
      <p className="admin-metric-card__label">{label}</p>
      <div className="admin-metric-card__value">
        <strong>—</strong>
        <span>{unit}</span>
      </div>
      <p className="admin-metric-card__note">{note}</p>
    </Card>
  )
}

type ChartLegendItemProps = {
  label: string
  tone: 'gaming' | 'products' | 'total' | 'expenses' | 'duration'
}

function ChartLegendItem({ label, tone }: ChartLegendItemProps) {
  return (
    <span className="admin-chart-legend__item">
      <i className={`admin-chart-legend__dot admin-chart-legend__dot--${tone}`} />
      {label}
    </span>
  )
}

function ChartEmptyState({
  icon: Icon,
  title,
  description,
  unit,
}: {
  icon: LucideIcon
  title: string
  description: string
  unit: string
}) {
  return (
    <div className="admin-chart-empty" role="status">
      <div className="admin-chart-empty__grid" aria-hidden="true" />
      <div className="admin-chart-empty__content">
        <span className="admin-chart-empty__icon"><Icon size={22} /></span>
        <strong>{title}</strong>
        <p>{description}</p>
        <small>وحدة القياس: {unit}</small>
      </div>
    </div>
  )
}

function EmptyTableRow({ columns, message }: { columns: number; message: string }) {
  return (
    <tr>
      <td colSpan={columns} className="admin-table-empty">{message}</td>
    </tr>
  )
}

function AdminAnalyticsTable({
  title,
  description,
  headers,
  emptyMessage,
  icon: Icon,
}: {
  title: string
  description: string
  headers: string[]
  emptyMessage: string
  icon: LucideIcon
}) {
  return (
    <Card className="admin-table-card">
      <CardHeader>
        <div>
          <CardTitle>{title}</CardTitle>
          <CardDescription>{description}</CardDescription>
        </div>
        <span className="admin-table-card__icon"><Icon size={19} /></span>
      </CardHeader>
      <div className="admin-table-scroll">
        <table className="admin-analytics-table">
          <thead>
            <tr>{headers.map((header) => <th key={header}>{header}</th>)}</tr>
          </thead>
          <tbody>
            <EmptyTableRow columns={headers.length} message={emptyMessage} />
          </tbody>
        </table>
      </div>
    </Card>
  )
}

function FinancialSummary() {
  const rows = [
    { label: 'إيرادات اللعب', tone: 'gaming', unit: 'ج.م' },
    { label: 'مبيعات المنتجات', tone: 'products', unit: 'ج.م' },
    { label: 'إجمالي التحصيل', tone: 'total', unit: 'ج.م' },
    { label: 'المصروفات والمرتجعات', tone: 'expenses', unit: 'ج.م' },
  ] as const

  return (
    <Card className="admin-financial-card">
      <CardHeader>
        <div>
          <CardTitle>ملخص الإيرادات</CardTitle>
          <CardDescription>فصل واضح بين دخل اللعب ومبيعات المنتجات</CardDescription>
        </div>
        <Badge tone="warning" dot>غير متصل</Badge>
      </CardHeader>
      <div className="admin-financial-rows">
        {rows.map((row) => (
          <div className="admin-financial-row" key={row.label}>
            <span className={`admin-financial-row__marker admin-financial-row__marker--${row.tone}`} />
            <span className="admin-financial-row__label">{row.label}</span>
            <strong>— <small>{row.unit}</small></strong>
          </div>
        ))}
      </div>
      <div className="admin-financial-note">
        <ShieldCheck size={16} />
        لن يتم احتساب صافي الربح قبل ربط تكاليف المنتجات والمصروفات الفعلية.
      </div>
    </Card>
  )
}

function ShiftCashSummary() {
  const items = [
    { label: 'الشيفت المفتوح', value: '—' },
    { label: 'النقدية المتوقعة', value: '— ج.م' },
    { label: 'النقدية الفعلية', value: '— ج.م' },
    { label: 'فرق الصندوق', value: '— ج.م' },
  ]

  return (
    <Card className="admin-shift-card">
      <CardHeader>
        <div>
          <CardTitle>الشيفت والخزينة</CardTitle>
          <CardDescription>مراجعة التحصيل والمطابقة</CardDescription>
        </div>
        <span className="admin-table-card__icon"><Wallet size={19} /></span>
      </CardHeader>
      <div className="admin-shift-rows">
        {items.map((item) => (
          <div className="admin-shift-row" key={item.label}>
            <span>{item.label}</span>
            <strong>{item.value}</strong>
          </div>
        ))}
      </div>
      <Badge tone="neutral">بانتظار بيانات الشيفت</Badge>
    </Card>
  )
}

function InsightsPanel() {
  return (
    <Card className="admin-insights-card">
      <CardHeader>
        <div>
          <CardTitle>التنبيهات ومؤشرات التشغيل</CardTitle>
          <CardDescription>تنبيهات مبنية على بيانات فعلية فقط</CardDescription>
        </div>
        <Activity size={19} className="admin-table-card__icon" />
      </CardHeader>
      <div className="admin-insights-empty">
        <span><ShieldCheck size={20} /></span>
        <div>
          <strong>محرك التحليلات لم يُربط بعد</strong>
          <p>ستظهر هنا تنبيهات انخفاض المخزون، ضغط الأجهزة، وفروقات الإيرادات بعد توصيل مصادر البيانات واختبارها.</p>
        </div>
      </div>
    </Card>
  )
}

export function AdminOverview() {
  const metrics: MetricCardProps[] = [
    { label: 'إجمالي التحصيل اليوم', icon: CircleDollarSign, tone: 'gold', unit: 'ج.م', note: 'اللعب + المنتجات بعد المطابقة' },
    { label: 'إيرادات اللعب', icon: Gamepad2, tone: 'blue', unit: 'ج.م', note: 'من الجلسات المغلقة والمدفوعة' },
    { label: 'مبيعات المنتجات', icon: CupSoda, tone: 'green', unit: 'ج.م', note: 'من فواتير نقاط البيع' },
    { label: 'ساعات اللعب', icon: Clock3, tone: 'violet', unit: 'ساعة', note: 'مدة اللعب في مقياس مستقل' },
    { label: 'عدد الجلسات', icon: Activity, tone: 'neutral', unit: 'جلسة', note: 'الجلسات المكتملة خلال الفترة' },
  ]

  return (
    <section className="admin-overview">
      <PageHeader
        eyebrow="إدارة GHOST · مركز المتابعة"
        title="نظرة عامة على المكان"
        description="صورة تشغيلية ومالية موحّدة، مع فصل إيرادات اللعب عن مبيعات المنتجات ووقت الاستخدام."
        actions={<Badge tone="warning" dot>البيانات الفعلية قيد الربط</Badge>}
      />

      <div className="admin-period-strip">
        <span><CalendarDays size={16} /> الفترة: اليوم</span>
        <p>سيتم تفعيل اختيار الفترة بعد تجهيز واجهات التقارير والتحقق من مطابقة الأرقام.</p>
      </div>

      <div className="admin-kpi-grid">
        {metrics.map((metric) => <MetricCard key={metric.label} {...metric} />)}
      </div>

      <div className="admin-analytics-grid">
        <Card className="admin-chart-card admin-chart-card--wide">
          <CardHeader>
            <div>
              <CardTitle>تحليل الإيرادات</CardTitle>
              <CardDescription>مقارنة مصادر الدخل خلال الفترة المحددة</CardDescription>
            </div>
            <Badge tone="neutral">ج.م</Badge>
          </CardHeader>
          <div className="admin-chart-legend">
            <ChartLegendItem label="اللعب" tone="gaming" />
            <ChartLegendItem label="المنتجات" tone="products" />
            <ChartLegendItem label="إجمالي التحصيل" tone="total" />
            <ChartLegendItem label="المصروفات والمرتجعات" tone="expenses" />
          </div>
          <ChartEmptyState
            icon={ChartNoAxesCombined}
            title="الرسم البياني جاهز لبياناتك"
            description="لن نرسم أعمدة أو خطوطًا تقديرية. ستظهر المقارنة بعد ربط الفواتير والتحصيل وسجل الإيرادات."
            unit="الجنيه المصري"
          />
        </Card>

        <Card className="admin-chart-card">
          <CardHeader>
            <div>
              <CardTitle>وقت اللعب والإشغال</CardTitle>
              <CardDescription>مدة الاستخدام حسب الغرفة والجهاز</CardDescription>
            </div>
            <Badge tone="violet">ساعات</Badge>
          </CardHeader>
          <div className="admin-chart-legend">
            <ChartLegendItem label="مدة اللعب" tone="duration" />
          </div>
          <ChartEmptyState
            icon={Clock3}
            title="تحليل الوقت منفصل"
            description="مدة اللعب والإشغال لهما مقياس زمني مستقل تمامًا عن الإيرادات."
            unit="ساعة / دقيقة"
          />
        </Card>
      </div>

      <div className="admin-secondary-grid">
        <FinancialSummary />
        <ShiftCashSummary />
      </div>

      <div className="admin-secondary-grid">
        <AdminAnalyticsTable
          title="أداء المنتجات"
          description="الكميات والمبيعات والتكلفة وهامش الربح عند توافر بيانات التكلفة"
          icon={Package}
          headers={['المنتج', 'الكمية', 'المبيعات', 'التكلفة', 'الهامش']}
          emptyMessage="لا توجد بيانات مبيعات معروضة حتى الآن."
        />
        <AdminAnalyticsTable
          title="الأجهزة والغرف"
          description="الجلسات وساعات التشغيل ونسبة الإشغال والإيراد"
          icon={Gamepad2}
          headers={['الجهاز / الغرفة', 'الجلسات', 'ساعات التشغيل', 'الإشغال', 'الإيراد']}
          emptyMessage="ستظهر بيانات الاستخدام بعد ربط استعلامات الجلسات."
        />
      </div>

      <div className="admin-secondary-grid admin-secondary-grid--bottom">
        <InsightsPanel />
        <div className="admin-data-integrity">
          <span className="admin-data-integrity__icon"><ShieldCheck size={20} /></span>
          <div>
            <strong>سلامة الأرقام قبل شكلها</strong>
            <p>كل رقم مالي سيُربط بمصدره الأصلي، ويمكن الانتقال من المؤشر إلى الفواتير أو الجلسات التي كوّنته. لا توجد قيم تجريبية في هذه الصفحة.</p>
          </div>
        </div>
      </div>
    </section>
  )
}
