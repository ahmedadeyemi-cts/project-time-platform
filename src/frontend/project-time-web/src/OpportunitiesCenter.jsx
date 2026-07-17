import { useEffect, useMemo, useState } from 'react';
import './opportunities-center.css';

const emptyOpportunity = {
  externalOpportunityId: '',
  sourceSystem: 'projectpulse',
  clientId: '',
  accountName: '',
  topic: '',
  ownerUserId: '',
  estimatedRevenue: '',
  actualRevenue: '',
  activeDate: new Date().toISOString().slice(0, 10),
  notes: ''
};

const emptyTask = {
  taskTitle: '',
  taskDescription: '',
  assignedRole: 'Engineer',
  assignedToUserId: '',
  dueDate: ''
};

async function apiJson(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(options.headers || {})
    }
  });

  const text = await response.text();
  const payload = text.trim() ? JSON.parse(text) : {};

  if (!response.ok) {
    throw new Error(
      payload.message
      || payload.status
      || `Request failed with HTTP ${response.status}`
    );
  }

  return payload;
}

function money(value) {
  if (value === null || value === undefined || value === '') return '—';

  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0
  }).format(Number(value || 0));
}

function date(value) {
  if (!value) return '—';

  const parsed = new Date(`${String(value).slice(0, 10)}T12:00:00`);

  return Number.isNaN(parsed.getTime())
    ? String(value)
    : parsed.toLocaleDateString();
}

function dateTime(value) {
  if (!value) return '—';

  const parsed = new Date(value);

  return Number.isNaN(parsed.getTime())
    ? String(value)
    : parsed.toLocaleString();
}

function displayUser(item) {
  return item?.displayName || item?.email || 'Unnamed user';
}

export default function OpportunitiesCenter() {
  const [access, setAccess] = useState({
    loading: true,
    canView: false,
    canManage: false,
    displayName: '',
    roles: [],
    error: ''
  });
  const [options, setOptions] = useState({
    customers: [],
    users: [],
    outcomes: ['won', 'lost', 'cancelled', 'other'],
    assignedRoles: ['Sales', 'Presales', 'Engineer']
  });
  const [scope, setScope] = useState('active');
  const [search, setSearch] = useState('');
  const [list, setList] = useState({
    loading: false,
    opportunities: [],
    error: ''
  });
  const [selectedId, setSelectedId] = useState('');
  const [detail, setDetail] = useState({
    loading: false,
    opportunity: null,
    tasks: [],
    events: [],
    error: ''
  });
  const [createForm, setCreateForm] = useState(emptyOpportunity);
  const [taskForm, setTaskForm] = useState(emptyTask);
  const [showCreate, setShowCreate] = useState(false);
  const [statusMessage, setStatusMessage] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [closeOutcome, setCloseOutcome] = useState('won');
  const [closedDate, setClosedDate] = useState(
    new Date().toISOString().slice(0, 10)
  );

  const selectedSummary = useMemo(
    () => list.opportunities.find(
      (item) => item.opportunityId === selectedId
    ) || null,
    [list.opportunities, selectedId]
  );

  const totals = useMemo(() => {
    const opportunities = list.opportunities || [];

    return opportunities.reduce(
      (summary, item) => ({
        openTasks: summary.openTasks + Number(item.openTaskCount || 0),
        completedTasks:
          summary.completedTasks + Number(item.completedTaskCount || 0),
        estimated:
          summary.estimated + Number(item.estimatedRevenue || 0),
        actual:
          summary.actual + Number(item.actualRevenue || 0)
      }),
      {
        openTasks: 0,
        completedTasks: 0,
        estimated: 0,
        actual: 0
      }
    );
  }, [list.opportunities]);

  useEffect(() => {
    let cancelled = false;

    async function loadAccess() {
      try {
        const result = await apiJson('/api/opportunities/access');

        if (cancelled) return;

        setAccess({
          loading: false,
          canView: Boolean(result.canView),
          canManage: Boolean(result.canManage),
          displayName: result.displayName || '',
          roles: result.roles || [],
          error: ''
        });

        if (!result.canView) return;

        const optionResult = await apiJson('/api/opportunities/options');

        if (cancelled) return;

        setOptions({
          customers: optionResult.customers || [],
          users: optionResult.users || [],
          outcomes:
            optionResult.outcomes
            || ['won', 'lost', 'cancelled', 'other'],
          assignedRoles:
            optionResult.assignedRoles
            || ['Sales', 'Presales', 'Engineer']
        });
      } catch (error) {
        if (cancelled) return;

        setAccess((current) => ({
          ...current,
          loading: false,
          error:
            error instanceof Error
              ? error.message
              : 'Unable to load Module 063 access.'
        }));
      }
    }

    loadAccess();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!access.canView) return;

    const timer = window.setTimeout(
      () => loadList(),
      150
    );

    return () => window.clearTimeout(timer);
  }, [access.canView, scope, search]);

  useEffect(() => {
    if (!selectedId || !access.canView) {
      setDetail({
        loading: false,
        opportunity: null,
        tasks: [],
        events: [],
        error: ''
      });
      return;
    }

    loadDetail(selectedId);
  }, [selectedId, access.canView]);

  async function loadList(preferredId = '') {
    setList((current) => ({
      ...current,
      loading: true,
      error: ''
    }));

    try {
      const query = new URLSearchParams({
        scope,
        search
      });

      const result = await apiJson(
        `/api/opportunities?${query.toString()}`
      );

      const opportunities = result.opportunities || [];

      setList({
        loading: false,
        opportunities,
        error: ''
      });

      const nextId =
        preferredId
        || (
          opportunities.some(
            (item) => item.opportunityId === selectedId
          )
            ? selectedId
            : opportunities[0]?.opportunityId || ''
        );

      setSelectedId(nextId);
    } catch (error) {
      setList({
        loading: false,
        opportunities: [],
        error:
          error instanceof Error
            ? error.message
            : 'Unable to load opportunities.'
      });
    }
  }

  async function loadDetail(opportunityId) {
    setDetail((current) => ({
      ...current,
      loading: true,
      error: ''
    }));

    try {
      const result = await apiJson(
        `/api/opportunities/${opportunityId}`
      );

      setDetail({
        loading: false,
        opportunity: result.opportunity || null,
        tasks: result.tasks || [],
        events: result.events || [],
        error: ''
      });
    } catch (error) {
      setDetail({
        loading: false,
        opportunity: null,
        tasks: [],
        events: [],
        error:
          error instanceof Error
            ? error.message
            : 'Unable to load opportunity details.'
      });
    }
  }

  async function createOpportunity(event) {
    event.preventDefault();

    if (!createForm.topic.trim()) {
      setStatusMessage('Enter an opportunity topic.');
      return;
    }

    if (!createForm.clientId && !createForm.accountName.trim()) {
      setStatusMessage('Select a customer or enter an account name.');
      return;
    }

    setIsSaving(true);
    setStatusMessage('');

    try {
      const result = await apiJson('/api/opportunities', {
        method: 'POST',
        body: JSON.stringify({
          ...createForm,
          clientId: createForm.clientId || null,
          ownerUserId: createForm.ownerUserId || null,
          estimatedRevenue:
            createForm.estimatedRevenue === ''
              ? null
              : Number(createForm.estimatedRevenue),
          actualRevenue:
            createForm.actualRevenue === ''
              ? null
              : Number(createForm.actualRevenue)
        })
      });

      setCreateForm(emptyOpportunity);
      setShowCreate(false);
      setScope('active');
      setStatusMessage('Opportunity created.');
      await loadList(result.opportunityId);
      await loadDetail(result.opportunityId);
    } catch (error) {
      setStatusMessage(
        error instanceof Error
          ? error.message
          : 'Unable to create opportunity.'
      );
    } finally {
      setIsSaving(false);
    }
  }

  async function updateOpportunity(patch, successMessage) {
    if (!selectedId) return;

    setIsSaving(true);
    setStatusMessage('');

    try {
      await apiJson(`/api/opportunities/${selectedId}`, {
        method: 'PATCH',
        body: JSON.stringify(patch)
      });

      setStatusMessage(successMessage);
      await loadList(selectedId);
      await loadDetail(selectedId);
    } catch (error) {
      setStatusMessage(
        error instanceof Error
          ? error.message
          : 'Unable to update opportunity.'
      );
    } finally {
      setIsSaving(false);
    }
  }

  async function createTask(event) {
    event.preventDefault();

    if (!selectedId || !taskForm.taskTitle.trim()) {
      setStatusMessage('Enter a task title.');
      return;
    }

    setIsSaving(true);
    setStatusMessage('');

    try {
      await apiJson(`/api/opportunities/${selectedId}/tasks`, {
        method: 'POST',
        body: JSON.stringify({
          ...taskForm,
          assignedToUserId: taskForm.assignedToUserId || null,
          dueDate: taskForm.dueDate || null
        })
      });

      setTaskForm(emptyTask);
      setStatusMessage('Task added.');
      await loadList(selectedId);
      await loadDetail(selectedId);
    } catch (error) {
      setStatusMessage(
        error instanceof Error
          ? error.message
          : 'Unable to create task.'
      );
    } finally {
      setIsSaving(false);
    }
  }

  async function updateTask(taskId, patch, successMessage) {
    if (!selectedId) return;

    setIsSaving(true);
    setStatusMessage('');

    try {
      await apiJson(
        `/api/opportunities/${selectedId}/tasks/${taskId}`,
        {
          method: 'PATCH',
          body: JSON.stringify(patch)
        }
      );

      setStatusMessage(successMessage);
      await loadList(selectedId);
      await loadDetail(selectedId);
    } catch (error) {
      setStatusMessage(
        error instanceof Error
          ? error.message
          : 'Unable to update task.'
      );
    } finally {
      setIsSaving(false);
    }
  }

  if (access.loading) {
    return (
      <div className="opportunity-center">
        <div className="opportunity-state">Loading Module 063...</div>
      </div>
    );
  }

  if (access.error) {
    return (
      <div className="opportunity-center">
        <div className="opportunity-alert error">{access.error}</div>
      </div>
    );
  }

  if (!access.canView) {
    return (
      <div className="opportunity-center">
        <div className="opportunity-hero">
          <div>
            <p>MODULE 063</p>
            <h1>Opportunities & Action Tracker</h1>
            <span>
              This page is available to Account Executives, Sales,
              Presales, Engineers, and Administrators.
            </span>
          </div>
        </div>
        <div className="opportunity-alert error">
          Your current role does not have Module 063 access.
        </div>
      </div>
    );
  }

  return (
    <div
      className="opportunity-center"
      data-module="063"
      data-route="opportunities"
    >
      <header className="opportunity-hero">
        <div>
          <p>MODULE 063</p>
          <h1>Opportunities & Action Tracker</h1>
          <span>
            Sales, Presales, and Engineering can create opportunities,
            add shared tasks, complete actions, and preserve who changed
            each record.
          </span>
        </div>

        <div className="opportunity-hero-actions">
          <span className="opportunity-user">
            Signed in as <strong>{access.displayName}</strong>
          </span>
          {access.canManage ? (
            <button
              type="button"
              className="primary"
              onClick={() => setShowCreate((current) => !current)}
            >
              {showCreate ? 'Cancel' : 'Add opportunity'}
            </button>
          ) : null}
        </div>
      </header>

      {statusMessage ? (
        <div className="opportunity-alert">{statusMessage}</div>
      ) : null}

      <section className="opportunity-summary-grid">
        <article>
          <span>{scope === 'active' ? 'Active opportunities' : 'Closed opportunities'}</span>
          <strong>{list.opportunities.length}</strong>
          <small>Current filtered view</small>
        </article>
        <article>
          <span>Open tasks</span>
          <strong>{totals.openTasks}</strong>
          <small>Actions still in progress</small>
        </article>
        <article>
          <span>Completed tasks</span>
          <strong>{totals.completedTasks}</strong>
          <small>Completed collaboration work</small>
        </article>
        <article>
          <span>{scope === 'closed' ? 'Actual revenue' : 'Estimated revenue'}</span>
          <strong>{money(scope === 'closed' ? totals.actual : totals.estimated)}</strong>
          <small>Across this filtered view</small>
        </article>
      </section>

      {showCreate ? (
        <form
          className="opportunity-create-form"
          onSubmit={createOpportunity}
        >
          <div className="opportunity-section-heading">
            <div>
              <p>New opportunity</p>
              <h2>Create an active opportunity</h2>
            </div>
            <span>Creator and timestamps are captured automatically.</span>
          </div>

          <div className="opportunity-form-grid">
            <label className="wide">
              Topic
              <input
                value={createForm.topic}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    topic: event.target.value
                  })
                }
                placeholder="Example: Security discovery and assessment"
                required
              />
            </label>

            <label>
              Customer Directory account
              <select
                value={createForm.clientId}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    clientId: event.target.value,
                    accountName: ''
                  })
                }
              >
                <option value="">Select customer</option>
                {options.customers.map((customer) => (
                  <option
                    key={customer.clientId}
                    value={customer.clientId}
                  >
                    {customer.customerName}
                  </option>
                ))}
              </select>
            </label>

            <label>
              Account name when not in directory
              <input
                value={createForm.accountName}
                disabled={Boolean(createForm.clientId)}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    accountName: event.target.value
                  })
                }
                placeholder="External or prospective account"
              />
            </label>

            <label>
              Opportunity owner
              <select
                value={createForm.ownerUserId}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    ownerUserId: event.target.value
                  })
                }
              >
                <option value="">Unassigned</option>
                {options.users.map((user) => (
                  <option key={user.userId} value={user.userId}>
                    {displayUser(user)}
                    {user.roleCode ? ` — ${user.roleCode}` : ''}
                  </option>
                ))}
              </select>
            </label>

            <label>
              Active date
              <input
                type="date"
                value={createForm.activeDate}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    activeDate: event.target.value
                  })
                }
              />
            </label>

            <label>
              Estimated revenue
              <input
                type="number"
                min="0"
                step="0.01"
                value={createForm.estimatedRevenue}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    estimatedRevenue: event.target.value
                  })
                }
              />
            </label>

            <label>
              Actual revenue
              <input
                type="number"
                min="0"
                step="0.01"
                value={createForm.actualRevenue}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    actualRevenue: event.target.value
                  })
                }
              />
            </label>

            <label>
              External opportunity ID
              <input
                value={createForm.externalOpportunityId}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    externalOpportunityId: event.target.value
                  })
                }
                placeholder="CRM or spreadsheet identifier"
              />
            </label>

            <label className="wide">
              Notes
              <textarea
                rows="3"
                value={createForm.notes}
                onChange={(event) =>
                  setCreateForm({
                    ...createForm,
                    notes: event.target.value
                  })
                }
                placeholder="Scope, next steps, customer needs, or handoff notes"
              />
            </label>
          </div>

          <div className="opportunity-form-actions">
            <button
              type="submit"
              className="primary"
              disabled={isSaving}
            >
              {isSaving ? 'Creating...' : 'Create opportunity'}
            </button>
          </div>
        </form>
      ) : null}

      <section className="opportunity-toolbar">
        <div className="opportunity-tabs" role="tablist">
          <button
            type="button"
            className={scope === 'active' ? 'active' : ''}
            onClick={() => setScope('active')}
          >
            Active
          </button>
          <button
            type="button"
            className={scope === 'closed' ? 'active' : ''}
            onClick={() => setScope('closed')}
          >
            Closed
          </button>
        </div>

        <label>
          Search
          <input
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Topic, account, owner, or opportunity ID"
          />
        </label>

        <button type="button" onClick={() => loadList(selectedId)}>
          Refresh
        </button>
      </section>

      {list.error ? (
        <div className="opportunity-alert error">{list.error}</div>
      ) : null}

      <div className="opportunity-workspace">
        <section className="opportunity-list-panel">
          <div className="opportunity-section-heading">
            <div>
              <p>{scope === 'active' ? 'Active pipeline' : 'Closed history'}</p>
              <h2>
                {scope === 'active'
                  ? 'Open opportunities'
                  : 'Completed opportunities'}
              </h2>
            </div>
            <span>{list.opportunities.length} records</span>
          </div>

          {list.loading ? (
            <div className="opportunity-state">Loading opportunities...</div>
          ) : list.opportunities.length === 0 ? (
            <div className="opportunity-state">
              No {scope} opportunities match the current search.
            </div>
          ) : (
            <div className="opportunity-card-list">
              {list.opportunities.map((item) => (
                <button
                  type="button"
                  key={item.opportunityId}
                  className={
                    item.opportunityId === selectedId
                      ? 'opportunity-card selected'
                      : 'opportunity-card'
                  }
                  onClick={() => setSelectedId(item.opportunityId)}
                >
                  <div className="opportunity-card-heading">
                    <div>
                      <strong>{item.topic}</strong>
                      <span>{item.accountName}</span>
                    </div>
                    <em className={`status ${item.status}`}>
                      {item.status}
                    </em>
                  </div>

                  <div className="opportunity-card-meta">
                    <span>Owner: {item.ownerName || 'Unassigned'}</span>
                    <span>Active: {date(item.activeDate)}</span>
                    {item.closedDate ? (
                      <span>Closed: {date(item.closedDate)}</span>
                    ) : null}
                    <span>Updated: {dateTime(item.updatedAt)}</span>
                  </div>

                  <div className="opportunity-card-footer">
                    <span>{item.openTaskCount} open task(s)</span>
                    <span>{item.completedTaskCount} completed</span>
                    <strong>
                      {money(
                        item.status === 'closed'
                          ? item.actualRevenue
                          : item.estimatedRevenue
                      )}
                    </strong>
                  </div>
                </button>
              ))}
            </div>
          )}
        </section>

        <section className="opportunity-detail-panel">
          {!selectedId ? (
            <div className="opportunity-state">
              Select an opportunity to review its tasks and history.
            </div>
          ) : detail.loading ? (
            <div className="opportunity-state">Loading details...</div>
          ) : detail.error ? (
            <div className="opportunity-alert error">{detail.error}</div>
          ) : detail.opportunity ? (
            <>
              <div className="opportunity-detail-heading">
                <div>
                  <p>{detail.opportunity.accountName}</p>
                  <h2>{detail.opportunity.topic}</h2>
                  <span>
                    Created by {detail.opportunity.createdByName}
                    {' • '}
                    Last updated by {detail.opportunity.updatedByName}
                  </span>
                </div>
                <em className={`status ${detail.opportunity.status}`}>
                  {detail.opportunity.status}
                </em>
              </div>

              <div className="opportunity-facts">
                <article>
                  <span>Owner</span>
                  <strong>
                    {detail.opportunity.ownerName || 'Unassigned'}
                  </strong>
                </article>
                <article>
                  <span>Active date</span>
                  <strong>{date(detail.opportunity.activeDate)}</strong>
                </article>
                <article>
                  <span>Closed date</span>
                  <strong>{date(detail.opportunity.closedDate)}</strong>
                </article>
                <article>
                  <span>Last updated</span>
                  <strong>{dateTime(detail.opportunity.updatedAt)}</strong>
                </article>
                <article>
                  <span>Estimated revenue</span>
                  <strong>{money(detail.opportunity.estimatedRevenue)}</strong>
                </article>
                <article>
                  <span>Actual revenue</span>
                  <strong>{money(detail.opportunity.actualRevenue)}</strong>
                </article>
              </div>

              {detail.opportunity.notes ? (
                <div className="opportunity-notes">
                  <strong>Notes</strong>
                  <p>{detail.opportunity.notes}</p>
                </div>
              ) : null}

              {access.canManage ? (
                <div className="opportunity-lifecycle-actions">
                  {detail.opportunity.status === 'active' ? (
                    <>
                      <label>
                        Close outcome
                        <select
                          value={closeOutcome}
                          onChange={(event) =>
                            setCloseOutcome(event.target.value)
                          }
                        >
                          {options.outcomes.map((outcome) => (
                            <option key={outcome} value={outcome}>
                              {outcome}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label>
                        Closed date
                        <input
                          type="date"
                          value={closedDate}
                          onChange={(event) =>
                            setClosedDate(event.target.value)
                          }
                        />
                      </label>
                      <button
                        type="button"
                        className="danger"
                        disabled={isSaving}
                        onClick={() =>
                          updateOpportunity(
                            {
                              status: 'closed',
                              closeOutcome,
                              closedDate
                            },
                            'Opportunity closed.'
                          )
                        }
                      >
                        Close opportunity
                      </button>
                    </>
                  ) : (
                    <button
                      type="button"
                      className="primary"
                      disabled={isSaving}
                      onClick={() =>
                        updateOpportunity(
                          { status: 'active' },
                          'Opportunity reopened.'
                        )
                      }
                    >
                      Reopen opportunity
                    </button>
                  )}
                </div>
              ) : null}

              <div className="opportunity-section-heading task-heading">
                <div>
                  <p>Shared action list</p>
                  <h3>Opportunity tasks</h3>
                </div>
                <span>
                  {detail.tasks.filter(
                    (task) => task.taskStatus === 'open'
                  ).length} open
                </span>
              </div>

              {access.canManage ? (
                <form
                  className="opportunity-task-form"
                  onSubmit={createTask}
                >
                  <label className="wide">
                    Task
                    <input
                      value={taskForm.taskTitle}
                      onChange={(event) =>
                        setTaskForm({
                          ...taskForm,
                          taskTitle: event.target.value
                        })
                      }
                      placeholder="What needs to be completed?"
                      required
                    />
                  </label>

                  <label>
                    Role
                    <select
                      value={taskForm.assignedRole}
                      onChange={(event) =>
                        setTaskForm({
                          ...taskForm,
                          assignedRole: event.target.value
                        })
                      }
                    >
                      {options.assignedRoles.map((role) => (
                        <option key={role} value={role}>
                          {role}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Assigned user
                    <select
                      value={taskForm.assignedToUserId}
                      onChange={(event) =>
                        setTaskForm({
                          ...taskForm,
                          assignedToUserId: event.target.value
                        })
                      }
                    >
                      <option value="">Unassigned</option>
                      {options.users.map((user) => (
                        <option key={user.userId} value={user.userId}>
                          {displayUser(user)}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label>
                    Due date
                    <input
                      type="date"
                      value={taskForm.dueDate}
                      onChange={(event) =>
                        setTaskForm({
                          ...taskForm,
                          dueDate: event.target.value
                        })
                      }
                    />
                  </label>

                  <label className="wide">
                    Description
                    <textarea
                      rows="2"
                      value={taskForm.taskDescription}
                      onChange={(event) =>
                        setTaskForm({
                          ...taskForm,
                          taskDescription: event.target.value
                        })
                      }
                    />
                  </label>

                  <button
                    type="submit"
                    className="primary"
                    disabled={isSaving}
                  >
                    Add task
                  </button>
                </form>
              ) : null}

              <div className="opportunity-task-list">
                {detail.tasks.length === 0 ? (
                  <div className="opportunity-state">
                    No tasks have been added.
                  </div>
                ) : (
                  detail.tasks.map((task) => (
                    <article
                      key={task.opportunityTaskId}
                      className={`opportunity-task ${task.taskStatus}`}
                    >
                      <div className="opportunity-task-main">
                        <div>
                          <strong>{task.taskTitle}</strong>
                          <span>
                            {task.assignedRole || 'Shared'}
                            {task.assignedToName
                              ? ` • ${task.assignedToName}`
                              : ''}
                            {task.dueDate
                              ? ` • Due ${date(task.dueDate)}`
                              : ''}
                          </span>
                        </div>
                        <em>{task.taskStatus}</em>
                      </div>

                      {task.taskDescription ? (
                        <p>{task.taskDescription}</p>
                      ) : null}

                      <small>
                        Added by {task.createdByName}
                        {' • '}
                        Updated {dateTime(task.updatedAt)}
                        {task.completedByName
                          ? ` • Completed by ${task.completedByName}`
                          : ''}
                      </small>

                      {access.canManage ? (
                        <div className="opportunity-task-actions">
                          {task.taskStatus === 'open' ? (
                            <button
                              type="button"
                              className="primary"
                              disabled={isSaving}
                              onClick={() =>
                                updateTask(
                                  task.opportunityTaskId,
                                  { taskStatus: 'completed' },
                                  'Task marked completed.'
                                )
                              }
                            >
                              Mark completed
                            </button>
                          ) : task.taskStatus === 'completed' ? (
                            <button
                              type="button"
                              disabled={isSaving}
                              onClick={() =>
                                updateTask(
                                  task.opportunityTaskId,
                                  { taskStatus: 'open' },
                                  'Task reopened.'
                                )
                              }
                            >
                              Reopen
                            </button>
                          ) : null}
                        </div>
                      ) : null}
                    </article>
                  ))
                )}
              </div>

              <details className="opportunity-history">
                <summary>Activity history ({detail.events.length})</summary>
                <div>
                  {detail.events.map((event) => (
                    <article key={event.opportunityEventId}>
                      <strong>
                        {String(event.eventType || '')
                          .replaceAll('_', ' ')}
                      </strong>
                      <span>
                        {event.actorName} • {dateTime(event.createdAt)}
                      </span>
                    </article>
                  ))}
                </div>
              </details>
            </>
          ) : (
            <div className="opportunity-state">
              {selectedSummary
                ? 'The selected opportunity could not be loaded.'
                : 'Select an opportunity.'}
            </div>
          )}
        </section>
      </div>
    </div>
  );
}
