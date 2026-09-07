// ─────────────────────────────────────────────────────────────────────────────
// Bikram Sambat (BS) ⇄ Gregorian (AD) conversion and calendar helpers.
//
// Month lengths follow the tables published by the Nepal Calendar Determination
// Committee. Years 2070–2095 are settled. 2096–2100 come from the widely used
// community dataset and should be re-checked against the official patro before
// anyone runs payroll that far ahead.
// ─────────────────────────────────────────────────────────────────────────────

export const REFERENCE_AD_DATE = new Date(2023, 3, 14) // 2023-04-14 = 1 Baisakh 2080
export const START_BS_YEAR = 2080
export const MIN_BS_YEAR = 2070
export const MAX_BS_YEAR = 2100

export const BS_MONTH_DAYS = {
  2070: [31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30],
  2071: [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30],
  2072: [31, 32, 31, 32, 31, 30, 30, 29, 30, 29, 30, 30],
  2073: [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31],
  2074: [31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30],
  2075: [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30],
  2076: [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30],
  2077: [31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31],
  2078: [31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30],
  2079: [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30],
  2080: [31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30],
  2081: [31, 31, 32, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2082: [30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2083: [31, 31, 32, 31, 31, 30, 30, 30, 29, 30, 30, 30],
  2084: [31, 31, 32, 32, 30, 31, 30, 30, 29, 30, 30, 30],
  2085: [31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2086: [31, 31, 32, 31, 31, 30, 30, 30, 29, 30, 30, 30],
  2087: [31, 31, 32, 31, 31, 30, 30, 30, 29, 30, 30, 30],
  2088: [30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2089: [31, 31, 32, 31, 31, 30, 30, 30, 29, 30, 30, 30],
  2090: [31, 31, 32, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2091: [31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2092: [30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2093: [31, 31, 32, 31, 31, 30, 30, 30, 29, 30, 30, 30],
  2094: [31, 31, 32, 32, 30, 31, 30, 30, 29, 30, 30, 30],
  2095: [31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2096: [30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30],
  2097: [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30],
  2098: [31, 31, 32, 31, 31, 31, 29, 30, 29, 30, 29, 31],
  2099: [31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30],
  2100: [31, 32, 31, 32, 30, 31, 30, 29, 30, 29, 30, 30],
}

export const MONTH_NAMES_EN = [
  'Baisakh', 'Jestha', 'Ashadh', 'Shrawan', 'Bhadra', 'Ashwin',
  'Kartik', 'Mangsir', 'Poush', 'Magh', 'Falgun', 'Chaitra',
]

export const MONTH_NAMES_NP = [
  'बैशाख', 'जेठ', 'असार', 'साउन', 'भदौ', 'असोज',
  'कार्तिक', 'मंसिर', 'पुष', 'माघ', 'फागुन', 'चैत',
]

export const AD_MONTH_NAMES_EN = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

export const DAY_NAMES_EN = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
export const DAY_NAMES_NP = ['आइतबार', 'सोमबार', 'मंगलबार', 'बुधबार', 'बिहीबार', 'शुक्रबार', 'शनिबार']
export const DAY_SHORT_NP = ['आइत', 'सोम', 'मंगल', 'बुध', 'बिहि', 'शुक्र', 'शनि']
export const DAY_SHORT_EN = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

const pad = (n) => String(n).padStart(2, '0')

const NP_DIGITS = ['०', '१', '२', '३', '४', '५', '६', '७', '८', '९']

/** 2083 → "२०८३". Used for the Hamro-Patro style calendar face. */
export function toNepaliDigits(value) {
  return String(value ?? '').replace(/\d/g, (d) => NP_DIGITS[Number(d)])
}

export function getDaysInBsMonth(bsYear, bsMonth) {
  const months = BS_MONTH_DAYS[bsYear]
  if (!months || bsMonth < 1 || bsMonth > 12) return 30
  return months[bsMonth - 1]
}

// ── AD → BS ──────────────────────────────────────────────────────────────────

export function adToBs(dateInput) {
  if (!dateInput) return null
  let d
  if (dateInput instanceof Date) {
    d = dateInput
  } else if (typeof dateInput === 'string' && /^\d{4}-\d{2}-\d{2}/.test(dateInput)) {
    const [y, m, day] = dateInput.slice(0, 10).split('-').map(Number)
    d = new Date(y, m - 1, day)
  } else {
    d = new Date(dateInput)
  }
  if (Number.isNaN(d.getTime())) return null

  const inputUtc = Date.UTC(d.getFullYear(), d.getMonth(), d.getDate())
  const refUtc = Date.UTC(
    REFERENCE_AD_DATE.getFullYear(),
    REFERENCE_AD_DATE.getMonth(),
    REFERENCE_AD_DATE.getDate(),
  )

  let diffDays = Math.round((inputUtc - refUtc) / 86400000)

  let year = START_BS_YEAR
  let month = 1
  let day = 1

  if (diffDays >= 0) {
    while (diffDays > 0) {
      const daysThisMonth = getDaysInBsMonth(year, month)
      const remaining = daysThisMonth - day
      if (diffDays <= remaining) {
        day += diffDays
        diffDays = 0
      } else {
        diffDays -= remaining + 1
        day = 1
        month++
        if (month > 12) { month = 1; year++ }
      }
    }
  } else {
    while (diffDays < 0) {
      month--
      if (month < 1) { month = 12; year-- }
      const daysThisMonth = getDaysInBsMonth(year, month)
      if (Math.abs(diffDays) <= daysThisMonth) {
        day = daysThisMonth + diffDays + 1
        diffDays = 0
      } else {
        diffDays += daysThisMonth
      }
    }
  }

  const dayOfWeek = d.getDay()

  return {
    year,
    month,
    day,
    dayOfWeek,
    monthNameEn: MONTH_NAMES_EN[month - 1],
    monthNameNp: MONTH_NAMES_NP[month - 1],
    dayNameEn: DAY_NAMES_EN[dayOfWeek],
    dayNameNp: DAY_NAMES_NP[dayOfWeek],
    dateBs: `${year}-${pad(month)}-${pad(day)}`,
    /** dd/mm/yyyy, matching the rest of the app. */
    dmy: `${pad(day)}/${pad(month)}/${year}`,
    str: `${day} ${MONTH_NAMES_EN[month - 1]} ${year}`,
  }
}

// ── BS → AD ──────────────────────────────────────────────────────────────────

const REF_AD_MS = Date.UTC(2023, 3, 14)

function totalBsDays(year, month, day) {
  let total = 0
  for (let y = MIN_BS_YEAR; y < year; y++) {
    const months = BS_MONTH_DAYS[y]
    if (months) total += months.reduce((a, b) => a + b, 0)
  }
  const months = BS_MONTH_DAYS[year]
  if (months) for (let m = 1; m < month; m++) total += months[m - 1]
  return total + day
}

const REF_TOTAL_BS = totalBsDays(2080, 1, 1)

/** BS y/m/d → ISO 'yyyy-mm-dd' in AD. */
export function bsToAdIso(bsYear, bsMonth, bsDay) {
  const diff = totalBsDays(bsYear, bsMonth, bsDay) - REF_TOTAL_BS
  const ad = new Date(REF_AD_MS + diff * 86400000)
  return `${ad.getUTCFullYear()}-${pad(ad.getUTCMonth() + 1)}-${pad(ad.getUTCDate())}`
}

/** BS y/m/d → a local Date at midnight. */
export function bsToAdDate(bsYear, bsMonth, bsDay) {
  const iso = bsToAdIso(bsYear, bsMonth, bsDay)
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d)
}

// ── Month grid ───────────────────────────────────────────────────────────────

/**
 * Builds one month of calendar cells, in either calendar system, so the same
 * grid component can render a BS month or an AD month.
 *
 *   mode  'BS' | 'AD'
 *   year  BS year (2080…) or AD year (2026…)
 *   month 1-12 in the chosen system
 *
 * Every cell carries BOTH representations, because the calendar always shows
 * the big number in the active system and the small number in the other one —
 * that is what makes it readable the way Hamro Patro is.
 */
export function buildMonthGrid(mode, year, month) {
  const cells = []
  const isBs = mode === 'BS'

  const daysInMonth = isBs
    ? getDaysInBsMonth(year, month)
    : new Date(year, month, 0).getDate()

  const firstDate = isBs
    ? bsToAdDate(year, month, 1)
    : new Date(year, month - 1, 1)

  const leading = firstDate.getDay()
  for (let i = 0; i < leading; i++) cells.push(null)

  for (let d = 1; d <= daysInMonth; d++) {
    const adDate = isBs
      ? bsToAdDate(year, month, d)
      : new Date(year, month - 1, d)
    const bs = adToBs(adDate)
    cells.push({
      adDate,
      adIso: `${adDate.getFullYear()}-${pad(adDate.getMonth() + 1)}-${pad(adDate.getDate())}`,
      adDay: adDate.getDate(),
      adMonth: adDate.getMonth() + 1,
      adYear: adDate.getFullYear(),
      bsDay: bs.day,
      bsMonth: bs.month,
      bsYear: bs.year,
      bsIso: bs.dateBs,
      weekday: adDate.getDay(),
      isSaturday: adDate.getDay() === 6,
      /** The number shown large, in the active system. */
      primaryDay: isBs ? bs.day : adDate.getDate(),
      /** The number shown small, in the other system. */
      secondaryDay: isBs ? adDate.getDate() : bs.day,
    })
  }

  // Trailing blanks so every month renders as whole weeks.
  while (cells.length % 7 !== 0) cells.push(null)

  return {
    cells,
    daysInMonth,
    titlePrimary: isBs
      ? `${MONTH_NAMES_NP[month - 1]} ${toNepaliDigits(year)}`
      : `${AD_MONTH_NAMES_EN[month - 1]} ${year}`,
    titleSecondary: isBs
      ? `${MONTH_NAMES_EN[month - 1]} ${year} BS`
      : `${AD_MONTH_NAMES_EN[month - 1]} ${year} AD`,
    /** The AD (or BS) span the month straddles, e.g. "Aug – Sep 2026". */
    spanLabel: (() => {
      const real = cells.filter(Boolean)
      if (real.length === 0) return ''
      const a = real[0]
      const b = real[real.length - 1]
      if (isBs) {
        const m1 = AD_MONTH_NAMES_EN[a.adMonth - 1].slice(0, 3)
        const m2 = AD_MONTH_NAMES_EN[b.adMonth - 1].slice(0, 3)
        return a.adMonth === b.adMonth
          ? `${m1} ${a.adYear}`
          : `${m1} – ${m2} ${b.adYear}`
      }
      const m1 = MONTH_NAMES_EN[a.bsMonth - 1]
      const m2 = MONTH_NAMES_EN[b.bsMonth - 1]
      return a.bsMonth === b.bsMonth
        ? `${m1} ${a.bsYear} BS`
        : `${m1} – ${m2} ${b.bsYear} BS`
    })(),
  }
}

/** Step a {year, month} pair by ±1 month within the chosen system. */
export function shiftMonth(mode, year, month, delta) {
  let y = year
  let m = month + delta
  while (m > 12) { m -= 12; y += 1 }
  while (m < 1) { m += 12; y -= 1 }
  if (mode === 'BS') {
    if (y < MIN_BS_YEAR) return { year: MIN_BS_YEAR, month: 1 }
    if (y > MAX_BS_YEAR) return { year: MAX_BS_YEAR, month: 12 }
  }
  return { year: y, month: m }
}
