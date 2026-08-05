import { useMemo, useState } from 'react';
import {
  DECISION_QUADRANTS,
  KANBAN_COLUMNS,
  addDays,
  calendarTasksInRange,
  daysBetween,
  iso,
  parseDateOnly,
  projectedOccurrenceDates,
  recurrenceSummary,
  shiftedSchedule,
  shortDate,
  taskCode,
  taskDecision,
  taskEnd,
  taskEstimate,
  taskId,
  taskKanban,
  taskName,
  taskOccursOn,
  taskProgress,
  taskSource,
  taskStart,
  taskStatus,
  title,
  toDateOnly
} from './projectForgeModel.js';

function allowed(task, capability, fallback) {
  return task?.[capability] === undefined ? Boolean(fallback) : Boolean(task[capability]);
}

function dragStart(event, task) {
  event.dataTransfer.effectAllowed = 'move';
  event.dataTransfer.setData('text/project-forge-task', `${taskSource(task)}:${taskId(task)}`);
}

function draggedTask(event, tasks) {
  const key = event.dataTransfer.getData('text/project-forge-task');
  return (tasks || []).find((task) => `${taskSource(task)}:${taskId(task)}` === key) || null;
}

export function Progress({ value }) {
  const progress = Math.max(0, Math.min(100, Number(value || 0)));
  return (
    <div className="forge-progress" aria-label={`${progress}% complete`}>
      <span style={{ width: `${progress}%` }} />
      <b>{Math.round(progress)}%</b>
    </div>
  );
}

export function Empty({ children = 'No live records match this view.' }) {
  return <div className="forge-empty">{children}</div>;
}

export function Metric({ label, value, hint }) {
  return (
    <article className="forge-metric">
      <span>{label}</span>
      <strong>{value}</strong>
      {hint ? <small>{hint}</small> : null}
    </article>
  );
}

export function TaskTable({ tasks, onOpenTask, showDecision = false, showRecurrence = false }) {
  if (!tasks.length) return <Empty />;
  return (
    <div className="forge-table-wrap">
      <table className="forge-table">
        <thead>
          <tr>
            <th>Task</th><th>Source</th><th>Phase</th><th>Status</th><th>Owner / reviewer</th>
            <th>Start</th><th>Due</th><th>Estimate</th><th>Progress</th>
            {showDecision ? <th>Decision</th> : null}{showRecurrence ? <th>Recurrence projection</th> : null}<th><span className="forge-visually-hidden">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          {tasks.map((task) => (
            <tr key={`${taskSource(task)}:${taskId(task)}`}>
              <td><b>{taskCode(task)}</b><span>{taskName(task)}</span></td>
              <td><span className={`forge-source-badge ${taskSource(task)}`}>{taskSource(task) === 'review_plan' ? 'Review plan' : 'Live'}</span></td>
              <td>{task.phaseName || task.phase || 'Unphased'}</td>
              <td><span className={`forge-pill ${taskStatus(task)}`}>{title(taskStatus(task))}</span></td>
              <td>{task.primaryAssigneeName || task.assigneeName || task.reviewerName || 'No active assignment'}</td>
              <td>{shortDate(taskStart(task))}</td>
              <td>{shortDate(taskEnd(task))}</td>
              <td>{Number(taskEstimate(task)).toLocaleString(undefined, { maximumFractionDigits: 2 })}h</td>
              <td><Progress value={taskProgress(task)} /></td>
              {showDecision ? <td>{title(taskDecision(task))}</td> : null}
              {showRecurrence ? <td><span>{recurrenceSummary(task)}</span><small className="forge-projection">Next: {projectedOccurrenceDates(task).map(shortDate).join(' · ') || 'No future dates'}</small></td> : null}
              <td><button className="forge-inline-button" type="button" onClick={() => onOpenTask(task)}>Open</button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function CalendarTask({ task, tasks, canManage, onOpenTask, onMoveSchedule }) {
  const projected = Boolean(task.recurrenceProjection);
  const canMove = !projected && allowed(task, 'canEditSchedule', canManage);
  return (
    <div
      className={`forge-calendar-task ${taskStatus(task)}`}
      draggable={canMove}
      onDragStart={(event) => dragStart(event, task)}
    >
      <button type="button" onClick={() => onOpenTask(task.recurrenceCanonicalTask || task)} title={taskName(task)}>
        <b>{taskCode(task)}</b> · {taskName(task)}
      </button>
      {projected ? <small className="forge-calendar-projection">Projected occurrence</small> : null}
      {canMove ? (
        <label>
          <span>Move due date</span>
          <input
            type="date"
            value={taskEnd(task) || taskStart(task)}
            onChange={(event) => {
              if (!event.target.value) return;
              const next = shiftedSchedule(task, event.target.value);
              onMoveSchedule(task, next.startDate, next.dueDate, 'move');
            }}
          />
        </label>
      ) : null}
    </div>
  );
}

function CalendarDay({ date, tasks, holidays = [], canManage, onOpenTask, onMoveSchedule, label }) {
  const rows = tasks.filter((task) => taskOccursOn(task, date));
  const canDrop = tasks.some((task) => allowed(task, 'canEditSchedule', canManage));
  return (
    <article
      className="forge-calendar-day"
      onDragOver={(event) => { if (canDrop) event.preventDefault(); }}
      onDrop={(event) => {
        event.preventDefault();
        const task = draggedTask(event, tasks);
        if (!task || !allowed(task, 'canEditSchedule', canManage)) return;
        const next = shiftedSchedule(task, date);
        onMoveSchedule(task, next.startDate, next.dueDate, 'move');
      }}
    >
      <strong>{label}</strong>
      {holidays.map((holiday) => <span className="holiday" key={holiday.companyHolidayId || holiday.holidayId || holiday.holidayDate || holiday.date}>{holiday.holidayName || holiday.name}</span>)}
      {rows.slice(0, 5).map((task) => <CalendarTask key={`${taskSource(task)}:${taskId(task)}:${task.recurrenceOccurrenceDate || 'canonical'}`} task={task} tasks={tasks} canManage={canManage} onOpenTask={onOpenTask} onMoveSchedule={onMoveSchedule} />)}
      {rows.length > 5 ? <small className="forge-calendar-more">+{rows.length - 5} more scheduled tasks</small> : null}
    </article>
  );
}

export function CalendarMonth({ tasks, holidays, canManage, onOpenTask, onMoveSchedule }) {
  const [cursor, setCursor] = useState(() => new Date());
  const year = cursor.getFullYear();
  const month = cursor.getMonth();
  const first = new Date(year, month, 1, 12);
  const days = new Date(year, month + 1, 0, 12).getDate();
  const cells = Array.from({ length: first.getDay() + days }, (_, index) => index < first.getDay() ? null : index - first.getDay() + 1);
  const visibleStart = toDateOnly(new Date(year, month, 1, 12));
  const visibleEnd = toDateOnly(new Date(year, month, days, 12));
  const calendarTasks = calendarTasksInRange(tasks, visibleStart, visibleEnd);
  return (
    <>
      <div className="forge-calendar-controls">
        <button type="button" onClick={() => setCursor(new Date(year, month - 1, 1, 12))}>Previous</button>
        <h3>{cursor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })}</h3>
        <button type="button" onClick={() => setCursor(new Date(year, month + 1, 1, 12))}>Next</button>
      </div>
      <p className="forge-interaction-help">Open a task for full details, drag it to another day, or use its Move due date control.</p>
      <div className="forge-calendar-scroll">
        <div className="forge-month-grid">
          {['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map((day) => <b className="forge-weekday" key={day}>{day}</b>)}
          {cells.map((day, index) => {
            if (!day) return <div key={`blank-${index}`} className="blank" />;
            const date = toDateOnly(new Date(year, month, day, 12));
            const dayHolidays = holidays.filter((holiday) => iso(holiday.holidayDate || holiday.date) === date);
            return <CalendarDay key={date} date={date} label={day} tasks={calendarTasks} holidays={dayHolidays} canManage={canManage} onOpenTask={onOpenTask} onMoveSchedule={onMoveSchedule} />;
          })}
        </div>
      </div>
    </>
  );
}

export function CalendarWeek({ tasks, holidays = [], canManage, onOpenTask, onMoveSchedule }) {
  const [offset, setOffset] = useState(0);
  const start = useMemo(() => {
    const value = new Date();
    value.setHours(12, 0, 0, 0);
    value.setDate(value.getDate() - value.getDay() + (offset * 7));
    return value;
  }, [offset]);
  const days = Array.from({ length: 7 }, (_, index) => {
    const value = new Date(start);
    value.setDate(start.getDate() + index);
    return value;
  });
  const calendarTasks = calendarTasksInRange(tasks, toDateOnly(days[0]), toDateOnly(days[6]));
  return (
    <>
      <div className="forge-calendar-controls">
        <button type="button" onClick={() => setOffset((value) => value - 1)}>Previous week</button>
        <h3>{shortDate(toDateOnly(days[0]))} – {shortDate(toDateOnly(days[6]))}</h3>
        <button type="button" onClick={() => setOffset((value) => value + 1)}>Next week</button>
      </div>
      <p className="forge-interaction-help">Drag scheduled work between days or use the date control on each task.</p>
      <div className="forge-calendar-scroll">
        <div className="forge-week-grid">
          {days.map((day) => {
            const date = toDateOnly(day);
            const dayHolidays = holidays.filter((holiday) => iso(holiday.holidayDate || holiday.date) === date);
            return <CalendarDay key={date} date={date} label={day.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' })} tasks={calendarTasks} holidays={dayHolidays} canManage={canManage} onOpenTask={onOpenTask} onMoveSchedule={onMoveSchedule} />;
          })}
        </div>
      </div>
    </>
  );
}

export function DecisionMatrix({ tasks, canManage, onOpenTask, onMoveDecision }) {
  return (
    <>
      <p className="forge-interaction-help">Drag between quadrants or use each task’s Move menu. “Delete” is a prioritization quadrant and never deletes a task.</p>
      <div className="forge-decision-grid">
        {DECISION_QUADRANTS.map((quadrant) => {
          const rows = tasks.filter((task) => taskDecision(task) === quadrant.id);
          return (
            <section
              key={quadrant.id}
              onDragOver={(event) => event.preventDefault()}
              onDrop={(event) => {
                event.preventDefault();
                const task = draggedTask(event, tasks);
                if (task && allowed(task, 'canEditDecision', canManage)) onMoveDecision(task, quadrant.id);
              }}
            >
              <h3>{quadrant.label}</h3><small>{quadrant.help}</small>
              {rows.length ? rows.map((task) => {
                const canMove = allowed(task, 'canEditDecision', canManage);
                return (
                  <article key={`${taskSource(task)}:${taskId(task)}`} draggable={canMove} onDragStart={(event) => dragStart(event, task)}>
                    <button type="button" className="forge-card-title" onClick={() => onOpenTask(task)}><b>{taskCode(task)}</b><span>{taskName(task)}</span></button>
                    <em>{taskEstimate(task)}h</em>
                    {canMove ? <label><span>Move</span><select aria-label={`Move ${taskName(task)} to decision quadrant`} value={quadrant.id} onChange={(event) => onMoveDecision(task, event.target.value)}>{DECISION_QUADRANTS.map((option) => <option key={option.id} value={option.id}>{option.label}</option>)}</select></label> : null}
                  </article>
                );
              }) : <Empty />}
            </section>
          );
        })}
      </div>
    </>
  );
}

export function KanbanBoard({ tasks, canManage, onOpenTask, onMoveWorkflow }) {
  const move = (task, category, position = {}) => {
    if (!task || taskKanban(task) === category && !position.beforeTaskId && !position.afterTaskId) return;
    onMoveWorkflow(task, category, position);
  };
  return (
    <>
      <p className="forge-interaction-help">Drag cards between or within columns. Keyboard and touch users can use each card’s Move menu.</p>
      <div className="forge-kanban" aria-label="Project task Kanban board">
        {KANBAN_COLUMNS.map((column) => {
          const rows = tasks.filter((task) => taskKanban(task) === column.id).sort((a, b) => Number(a.displayOrder || 0) - Number(b.displayOrder || 0));
          return (
            <section
              key={column.id}
              aria-label={`${column.label} tasks`}
              onDragOver={(event) => event.preventDefault()}
              onDrop={(event) => { event.preventDefault(); move(draggedTask(event, tasks), column.id); }}
            >
              <h3>{column.label} <span>{rows.length}</span></h3>
              {rows.map((task) => {
                const canMove = allowed(task, 'canEditWorkflow', canManage);
                return (
                  <article
                    key={`${taskSource(task)}:${taskId(task)}`}
                    draggable={canMove}
                    onDragStart={(event) => dragStart(event, task)}
                    onDragOver={(event) => { if (canMove) event.preventDefault(); }}
                    onDrop={(event) => {
                      event.preventDefault(); event.stopPropagation();
                      const source = draggedTask(event, tasks);
                      if (source && taskId(source) !== taskId(task)) move(source, column.id, { beforeTaskId: taskId(task) });
                    }}
                  >
                    <b>{taskCode(task)}</b>
                    <button type="button" className="forge-card-title" onClick={() => onOpenTask(task)}>{taskName(task)}</button>
                    <p>{task.taskDescription || task.description || 'No description.'}</p>
                    <Progress value={taskProgress(task)} />
                    <small>{task.primaryAssigneeName || task.assigneeName || 'No active assignment'} · {shortDate(taskEnd(task))}</small>
                    {canMove ? <label><span>Move</span><select aria-label={`Move ${taskName(task)} to Kanban column`} value={column.id} onChange={(event) => move(task, event.target.value)}>{KANBAN_COLUMNS.map((option) => <option key={option.id} value={option.id}>{option.label}</option>)}</select></label> : null}
                  </article>
                );
              })}
            </section>
          );
        })}
      </div>
    </>
  );
}

const ZOOM_LEVELS = Object.freeze({ day: 2, week: 14, month: 45 });

export function GanttChart({ tasks, dependencies = [], canManage, onOpenTask, onMoveSchedule }) {
  const [zoom, setZoom] = useState('week');
  const dated = tasks.filter((task) => taskStart(task) && taskEnd(task));
  if (!dated.length) return <Empty>No scheduled task dates are available for this project.</Empty>;
  const rawMin = dated.map((task) => parseDateOnly(taskStart(task))?.getTime()).filter(Number.isFinite);
  const rawMax = dated.map((task) => parseDateOnly(taskEnd(task))?.getTime()).filter(Number.isFinite);
  const padding = ZOOM_LEVELS[zoom];
  const min = Math.min(...rawMin) - padding * 86400000;
  const max = Math.max(...rawMax, min + 86400000) + padding * 86400000;
  const spanDays = Math.max(1, Math.round((max - min) / 86400000));
  const moveBy = (task, amount) => onMoveSchedule(task, addDays(taskStart(task), amount), addDays(taskEnd(task), amount), 'move');
  const resizeEnd = (task, amount) => {
    const candidate = addDays(taskEnd(task), amount);
    if (candidate && candidate >= taskStart(task)) onMoveSchedule(task, taskStart(task), candidate, 'resize_end');
  };
  return (
    <>
      <div className="forge-gantt-toolbar">
        <span>Zoom</span>
        {Object.keys(ZOOM_LEVELS).map((level) => <button type="button" key={level} className={zoom === level ? 'active' : ''} aria-pressed={zoom === level} onClick={() => setZoom(level)}>{title(level)}</button>)}
      </div>
      <p className="forge-interaction-help">Drag a bar to a new start date, or use the Move and Resize controls. Recurring tasks move as one series.</p>
      <div className="forge-gantt-viewport">
      <div className={`forge-gantt zoom-${zoom}`}>
        <div className="forge-gantt-scale"><span>{shortDate(toDateOnly(new Date(min)))}</span><span>{shortDate(toDateOnly(new Date(max)))}</span></div>
        {dated.map((task) => {
          const startMs = parseDateOnly(taskStart(task)).getTime();
          const endMs = parseDateOnly(taskEnd(task)).getTime();
          const left = ((startMs - min) / (max - min)) * 100;
          const width = Math.max(1.5, ((endMs - startMs + 86400000) / (max - min)) * 100);
          const canMove = allowed(task, 'canEditSchedule', canManage);
          const predecessors = dependencies.filter((edge) => String(edge.successorTaskId) === String(taskId(task)));
          return (
            <div className="forge-gantt-row" key={`${taskSource(task)}:${taskId(task)}`}>
              <button type="button" className="forge-card-title" onClick={() => onOpenTask(task)}><b>{taskCode(task)}</b><span>{taskName(task)}</span><small>{predecessors.length ? `${predecessors.length} predecessor${predecessors.length === 1 ? '' : 's'}` : 'No predecessors'}</small></button>
              <div
                className="forge-gantt-track"
                onDragOver={(event) => { if (canMove) event.preventDefault(); }}
                onDrop={(event) => {
                  event.preventDefault();
                  const source = draggedTask(event, tasks);
                  if (!source || !allowed(source, 'canEditSchedule', canManage)) return;
                  const bounds = event.currentTarget.getBoundingClientRect();
                  const ratio = Math.max(0, Math.min(1, (event.clientX - bounds.left) / bounds.width));
                  const startDate = toDateOnly(new Date(min + Math.round(ratio * spanDays) * 86400000));
                  const duration = Math.max(0, daysBetween(taskStart(source), taskEnd(source)));
                  onMoveSchedule(source, startDate, addDays(startDate, duration), 'move');
                }}
              >
                <span className={taskStatus(task)} draggable={canMove} onDragStart={(event) => dragStart(event, task)} style={{ left: `${left}%`, width: `${Math.min(width, 100 - left)}%` }}>{taskProgress(task)}%</span>
              </div>
              <div className="forge-gantt-actions">
                <small>{shortDate(taskStart(task))} – {shortDate(taskEnd(task))}</small>
                {canMove ? <div><button type="button" onClick={() => moveBy(task, -1)} aria-label={`Move ${taskName(task)} one day earlier`}>← Move</button><button type="button" onClick={() => moveBy(task, 1)} aria-label={`Move ${taskName(task)} one day later`}>Move →</button><button type="button" onClick={() => resizeEnd(task, -1)} aria-label={`Shorten ${taskName(task)} by one day`}>− Day</button><button type="button" onClick={() => resizeEnd(task, 1)} aria-label={`Extend ${taskName(task)} by one day`}>+ Day</button></div> : null}
              </div>
            </div>
          );
        })}
      </div>
      </div>
    </>
  );
}
