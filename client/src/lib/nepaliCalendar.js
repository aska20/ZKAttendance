// Bikram Sambat (BS) / Nepali Calendar converter & date utilities
// Uses official Ministry of Information Nepal Patro month length tables.

export const REFERENCE_AD_DATE = new Date(2023, 3, 14) // 2023-04-14 is Baisakh 1, 2080 BS
export const START_BS_YEAR = 2080

// Month days for BS years 2070 - 2095
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
}

export const MONTH_NAMES_EN = [
  'Baisakh', 'Jestha', 'Ashadh', 'Shrawan', 'Bhadra', 'Ashwin',
  'Kartik', 'Mangsir', 'Poush', 'Magh', 'Falgun', 'Chaitra',
]

export const MONTH_NAMES_NP = [
  'बैशाख', 'जेठ', 'असार', 'साउन', 'भदौ', 'असोज',
  'कार्तिक', 'मंसिर', 'पुष', 'माघ', 'फागुन', 'चैत',
]

export const DAY_NAMES_EN = [
  'Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday',
]

export const DAY_NAMES_NP = [
  'आइतबार', 'सोमबार', 'मंगलबार', 'बुधबार', 'बिहीबार', 'शुक्रबार', 'शनिबार',
]

const pad = (n) => String(n).padStart(2, '0')

export function getDaysInBsMonth(bsYear, bsMonth) {
  const months = BS_MONTH_DAYS[bsYear]
  if (!months || bsMonth < 1 || bsMonth > 12) return 30
  return months[bsMonth - 1]
}

/**
 * Converts a Gregorian (AD) Date/ISO string to Bikram Sambat (BS).
 */
export function adToBs(dateInput) {
  if (!dateInput) return null
  const d = dateInput instanceof Date ? dateInput : new Date(dateInput)
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
        if (month > 12) {
          month = 1
          year++
        }
      }
    }
  } else {
    while (diffDays < 0) {
      month--
      if (month < 1) {
        month = 12
        year--
      }
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
  const monthNameEn = MONTH_NAMES_EN[month - 1]
  const monthNameNp = MONTH_NAMES_NP[month - 1]
  const dayNameEn = DAY_NAMES_EN[dayOfWeek]
  const dayNameNp = DAY_NAMES_NP[dayOfWeek]

  const dateBs = `${year}-${pad(month)}-${pad(day)}`
  const str = `${day} ${monthNameEn} ${year}`

  return {
    year,
    month,
    day,
    dayOfWeek,
    monthNameEn,
    monthNameNp,
    dayNameEn,
    dayNameNp,
    dateBs,
    str,
  }
}
