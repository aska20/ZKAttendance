/* ============================================================================
   RUN THIS BEFORE Update-Database

   The new unique index UX_Employee_Email will FAIL to create if two employees
   already share an email address. The migration then rolls back with

       "The CREATE UNIQUE INDEX statement terminated because a duplicate key
        was found for the object name 'dbo.Employees'"

   which is accurate but does not tell you who. This script does.
   ============================================================================ */

-- 1. Who shares an address?
SELECT
    LOWER(LTRIM(RTRIM(Email))) AS NormalisedEmail,
    COUNT(*)                   AS EmployeeCount,
    STRING_AGG(CAST(EmployeeId AS VARCHAR(10)) + ' ' + EmployeeName, ' | ') AS Employees
FROM Employees
WHERE Email IS NOT NULL AND LTRIM(RTRIM(Email)) <> ''
GROUP BY LOWER(LTRIM(RTRIM(Email)))
HAVING COUNT(*) > 1
ORDER BY EmployeeCount DESC;

-- 2. Addresses that differ only by case or whitespace. These are the same
--    address to a person, and the index will treat them as duplicates too,
--    because SQL Server's default collation is case-insensitive.
SELECT EmployeeId, EmployeeName, '[' + Email + ']' AS EmailWithBrackets
FROM Employees
WHERE Email IS NOT NULL
  AND (Email <> LTRIM(RTRIM(Email)) OR Email <> LOWER(Email))
ORDER BY EmployeeName;

-- 3. Blank-but-not-null addresses. These count as a value for the index, so
--    two of them collide, but they are not real addresses.
SELECT EmployeeId, EmployeeName
FROM Employees
WHERE Email IS NOT NULL AND LTRIM(RTRIM(Email)) = '';

GO

/* ----------------------------------------------------------------------------
   FIXES. Read the results above first, then run what applies.
   ---------------------------------------------------------------------------- */

-- FIX A: trim whitespace. Always safe.
UPDATE Employees
SET Email = LTRIM(RTRIM(Email))
WHERE Email IS NOT NULL AND Email <> LTRIM(RTRIM(Email));

-- FIX B: turn blank strings into NULL, which is what "no email" means.
-- The index is filtered on NOT NULL, so any number of employees may have none.
UPDATE Employees
SET Email = NULL
WHERE Email IS NOT NULL AND LTRIM(RTRIM(Email)) = '';

/* FIX C: genuine duplicates.

   NOT automated on purpose. Two employees sharing an address is either a data
   entry mistake or two people using one family address, and only you know
   which. Blanking the wrong one means that person stops receiving attendance
   emails silently.

   Decide per pair, then:

       UPDATE Employees SET Email = 'their.own@company.com' WHERE EmployeeId = 42;
       -- or, if they genuinely have no address of their own:
       UPDATE Employees SET Email = NULL WHERE EmployeeId = 42;

   An employee with NULL email is skipped by the mail run and counted under
   "Skipped" in the result, so nothing fails.
*/

GO

-- 4. Re-run this. Zero rows means Update-Database will succeed.
SELECT COUNT(*) AS RemainingDuplicateGroups
FROM (
    SELECT LOWER(LTRIM(RTRIM(Email))) AS e
    FROM Employees
    WHERE Email IS NOT NULL AND LTRIM(RTRIM(Email)) <> ''
    GROUP BY LOWER(LTRIM(RTRIM(Email)))
    HAVING COUNT(*) > 1
) dupes;
GO
