import { Card, Field, Select, Button } from '../ui'
import NepaliDatePicker from '../NepaliDatePicker'
import EmployeeSelect from './EmployeeSelect'

/**
 * Filter bar for the attendance page.
 * Props:
 *   range        — { from: string, to: string }
 *   onRangeChange — (range) => void
 *   departmentId — string
 *   onDeptChange  — (id: string) => void
 *   employeeId   — string
 *   onEmpChange   — (id: string) => void
 *   deptList     — Department[]
 *   employees    — Employee[]
 */
export default function AttendanceFilters({
  range, onRangeChange,
  departmentId, onDeptChange,
  employeeId, onEmpChange,
  deptList = [],
  employees = [],
}) {
  return (
    <Card className="mb-4 p-4">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3">
        <Field label="Start">
          <NepaliDatePicker
            value={range.from}
            onChange={(v) => onRangeChange({ ...range, from: v })}
          />
        </Field>

        <Field label="End">
          <NepaliDatePicker
            value={range.to}
            onChange={(v) => onRangeChange({ ...range, to: v })}
          />
        </Field>

        <Field label="Department">
          <Select value={departmentId} onChange={(e) => onDeptChange(e.target.value)}>
            <option value="">All departments</option>
            {deptList.map((d) => (
              <option key={d.departmentId} value={d.departmentId}>{d.departmentName}</option>
            ))}
          </Select>
        </Field>

        <Field label="Employee">
          <EmployeeSelect employees={employees} value={employeeId} onChange={onEmpChange} />
        </Field>

        <div className="flex items-end">
          <Button
            type="button"
            variant="ghost"
            className="w-full sm:w-auto"
            onClick={() => { onEmpChange(''); onDeptChange('') }}
          >
            Clear
          </Button>
        </div>
      </div>
    </Card>
  )
}
