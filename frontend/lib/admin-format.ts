export type DigitMode = 'western' | 'arabic'

const westernToArabic = (value: string) => value.replace(/[0-9]/g, (digit) => '٠١٢٣٤٥٦٧٨٩'[Number(digit)]).replaceAll(',', '٬').replaceAll('.', '٫')
const arabicToWestern = (value: string) => value.replace(/[٠-٩]/g, (digit) => String('٠١٢٣٤٥٦٧٨٩'.indexOf(digit))).replace(/[٬،]/g, ',').replaceAll('٫', '.')

export function formatDigits(value: string | number, mode: DigitMode = 'western') {
  const text = typeof value === 'number' ? new Intl.NumberFormat('en-US').format(value) : value
  return mode === 'arabic' ? westernToArabic(text) : arabicToWestern(text)
}

export function formatCurrency(value: number, mode: DigitMode = 'western') {
  const formatted = new Intl.NumberFormat('en-US', { maximumFractionDigits: 2 }).format(value)
  return `${formatDigits(formatted, mode)} ج.م`
}

export function formatDate(value: string, timezone: string, mode: DigitMode = 'western') {
  const date = new Date(`${value}T12:00:00Z`)
  if (Number.isNaN(date.getTime())) return formatDigits(value, mode)
  const locale = mode === 'arabic' ? 'ar-EG' : 'ar-EG-u-nu-latn'
  return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', year: 'numeric', timeZone: timezone }).format(date)
}
