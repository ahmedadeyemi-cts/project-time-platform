import { useMemo, useState } from 'react';
import './system-user-guide.css';
import { compareProjectPulseModules } from './module-ordering.js';

const roleOptions = [
  'All roles',
  'Everyone',
  'Engineer',
  'Manager',
  'Project Manager',
  'Project Team Coordinator',
  'Sales / Account Executive',
  'Presales',
  'Accounting',
  'Administrator'
];

const globalGuide = [
  {
    id: 'sign-in-session',
    category: 'Getting Started',
    title: 'Sign in, session, and security',
    audience: ['Everyone'],
    summary: 'How ProjectPulse identifies you, protects API requests, and handles session expiration.',
    functions: [
      'Sign in with the authentication method configured for the environment.',
      'A ProjectPulse session token is stored in the browser and attached to same-origin API requests.',
      'The session has an expiration time. Expired sessions require a new sign-in.',
      'Unauthenticated protected API calls return HTTP 401 and do not expose application data.',
      'Signing out removes the local ProjectPulse session.'
    ],
    steps: [
      'Open ProjectPulse and complete sign-in.',
      'Confirm your display name and role-aware workspace are shown.',
      'When the session warning appears, save work before the session expires.',
      'Sign in again when the application reports that a session is required.'
    ],
    notes: [
      'Never copy or share a session token.',
      'A browser refresh should preserve an unexpired session.',
      'Access to a page does not automatically grant permission to perform every action on that page.'
    ]
  },
  {
    id: 'navigation',
    category: 'Getting Started',
    title: 'Dashboard, navigation, and routes',
    audience: ['Everyone'],
    summary: 'How to move through the system and understand module cards, navigation groups, and browser routes.',
    functions: [
      'Dashboard cards show modules available to the signed-in role.',
      'Top navigation provides primary modules.',
      'More groups additional modules by business function.',
      'Each page uses a hash route such as #timesheet, #project-workspace, or #opportunities.',
      'The page-context guide identifies the current route and provides contextual guidance.',
      'A hard refresh reloads the currently selected route.'
    ],
    steps: [
      'Select Dashboard to return to the role workspace.',
      'Select a module card or navigation item.',
      'Use the browser Back button to return to the previous route.',
      'Use the page-context guide when you are uncertain about the current page.'
    ],
    notes: [
      'Module 999 is intentionally visible to every authenticated role.',
      'Other modules remain role- and permission-restricted.'
    ]
  },
  {
    id: 'global-search',
    category: 'Getting Started',
    title: 'PHD Search',
    audience: ['Everyone'],
    summary: 'Search modules, projects, customers, documents, assignments, people, teams, and reports.',
    functions: [
      'Open search from the top bar or press Ctrl+K on Windows/Linux or Command+K on macOS.',
      'Search loads role-appropriate records from Project Workspace, reporting filters, and the module catalog.',
      'Results show the item type, title, supporting context, and destination route.',
      'Selecting a result opens the relevant ProjectPulse page.'
    ],
    steps: [
      'Open PHD Search.',
      'Enter at least two characters.',
      'Use the arrow keys or pointer to select a result.',
      'Press Enter or select the result to navigate.'
    ],
    notes: [
      'Search results are limited by the APIs and permissions available to the current user.',
      'Search does not bypass module security.'
    ]
  },
  {
    id: 'profile-theme',
    category: 'Getting Started',
    title: 'Profile, preferences, and appearance',
    audience: ['Everyone'],
    summary: 'Personal profile settings, profile photo, display preferences, and light/dark appearance.',
    functions: [
      'Open the profile menu from the top bar.',
      'Review identity and role information.',
      'Store supported profile preferences, including profile photo where enabled.',
      'Switch between light and dark appearance.',
      'Timesheet view preferences and personal default rows are stored per user.'
    ],
    steps: [
      'Open the profile menu.',
      'Choose the relevant settings panel.',
      'Update supported fields and save.',
      'Use the appearance control to switch theme.'
    ],
    notes: [
      'Changing a display preference does not change your security role.',
      'Administrators manage role assignments through administrative modules.'
    ]
  },
  {
    id: 'role-access',
    category: 'Security & Access',
    title: 'Role-based access and permissions',
    audience: ['Everyone'],
    summary: 'Why different people see different modules and actions.',
    functions: [
      'Roles describe the user’s business responsibility.',
      'Permissions control page visibility and actions.',
      'Administrators may have MANAGE_ALL or SYSTEM_ADMINISTRATION.',
      'Module APIs independently verify access before returning or changing data.',
      'Buttons may be hidden or disabled when the current user cannot perform an action.'
    ],
    steps: [
      'Review the modules displayed on your Dashboard.',
      'Open Module 999 to understand modules that may exist outside your role.',
      'Contact an administrator when your assigned job requires additional access.',
      'Administrators should use the role and permission tools rather than editing browser storage.'
    ],
    notes: [
      'Module 999 explains all modules but does not grant access to them.',
      'Do not treat hidden buttons as a technical error until role requirements are confirmed.'
    ]
  },
  {
    id: 'view-as',
    category: 'Security & Access',
    title: 'Administrator View-As preview',
    audience: ['Administrator'],
    summary: 'Preview another user’s effective workspace without changing that user’s data.',
    functions: [
      'Administrators can select a user from View As.',
      'Read requests include the selected effective user identifier when the backend module supports it.',
      'Write actions are blocked during preview.',
      'Exit returns to My Administrator view.'
    ],
    steps: [
      'Select a user from View As.',
      'Review the navigation and pages visible to that user.',
      'Do not attempt production changes while previewing.',
      'Select Exit or My Administrator view when finished.'
    ],
    notes: [
      'View-As is a read-only troubleshooting and validation tool.',
      'Not every backend module necessarily honors the effective-user header.'
    ]
  },
  {
    id: 'help-tools',
    category: 'Help & Support',
    title: 'Help assistant and Module 999',
    audience: ['Everyone'],
    summary: 'Use quick help for a short answer and Module 999 for the complete guide.',
    functions: [
      'The Help launcher opens a small assistant for common questions.',
      'Open complete user guide navigates to Module 999.',
      'Module 999 provides searchable function-level documentation for global features and installed modules.',
      'The guide can be filtered by category and audience.',
      'Print creates a printer-friendly copy of the current guide results.'
    ],
    steps: [
      'Use Help for a quick question.',
      'Open Module 999 for detailed procedures and definitions.',
      'Search by module number, page title, button name, status, or business term.',
      'Open a guide entry and follow the listed steps.'
    ],
    notes: [
      'Module 999 is a user guide, not a replacement for access approval or production support escalation.'
    ]
  },
  {
    id: 'saving-audit',
    category: 'Data & Accountability',
    title: 'Saving, persistence, history, and audit',
    audience: ['Everyone'],
    summary: 'How the system records work and why creator, updater, approver, and completion details matter.',
    functions: [
      'Save actions send validated data to the API and database.',
      'Draft and submitted states have different editing rules.',
      'Workflow actions record responsible users and timestamps where supported.',
      'Audit History records selected security, administrative, approval, export, and operational events.',
      'Refreshing a page reloads persisted data; unsaved local changes may be lost.'
    ],
    steps: [
      'Complete all required fields.',
      'Select the page’s Save, Add, Submit, Approve, Complete, or Update action.',
      'Confirm the success message or updated status.',
      'Refresh only after the save has completed.'
    ],
    notes: [
      'A visible browser change is not proof of persistence until the API succeeds.',
      'Do not close the page while a save is still processing.'
    ]
  },
  {
    id: 'errors-troubleshooting',
    category: 'Help & Support',
    title: 'Errors, troubleshooting, and safe escalation',
    audience: ['Everyone'],
    summary: 'What to check when a page is missing, data does not save, or the application reports an error.',
    functions: [
      'HTTP 401 means a valid session is required.',
      'HTTP 403 means the signed-in identity does not have access to that action.',
      'HTTP 400 usually means a field or workflow validation failed.',
      'HTTP 404 means the requested record or route was not found.',
      'HTTP 409 indicates a state conflict or duplicate operation.',
      'HTTP 500 indicates an unexpected server-side failure.'
    ],
    steps: [
      'Read the displayed message and preserve the exact wording.',
      'Confirm the correct route and role.',
      'Refresh once after confirming your work was saved.',
      'Open browser Developer Tools and review failed Network requests when authorized to troubleshoot.',
      'Provide the route, timestamp, action, and HTTP status to support.'
    ],
    notes: [
      'Do not repeatedly submit the same write action when the result is unknown.',
      'Do not expose tokens, passwords, connection strings, or private customer data in screenshots.'
    ]
  },
  {
    id: 'session-intelligence',
    category: 'Platform Features',
    title: 'Session Intelligence drawer',
    audience: ['Everyone', 'Administrator'],
    summary: 'Review effective-session information and diagnostic context without leaving the current page.',
    functions: [
      'Open the Session Intelligence drawer where available.',
      'Review signed-in identity and effective role context.',
      'Use the information to understand authorization and session behavior.',
      'Close the drawer to return to the current workflow.'
    ],
    steps: [
      'Open Session Intelligence.',
      'Review the displayed identity and role context.',
      'Use the information when troubleshooting access.',
      'Close the drawer.'
    ],
    notes: [
      'Session Intelligence is diagnostic; it does not grant new permissions.'
    ]
  }
];

const detailedModuleGuides = {
  timesheet: {
    category: 'Time & Approvals',
    audience: ['Everyone', 'Engineer', 'Manager'],
    purpose: 'Enter, review, save, and submit project-task and non-project time.',
    functions: [
      'Weekly Grid shows the complete seven-day entry grid.',
      'Daily Focus provides a day-centered mobile-friendly entry view.',
      'Guided Add lets the user choose work, dates, hours, and description before adding entries.',
      'Quick Entry List provides compact activity entry.',
      'Smart Work Log converts rough work notes into review cards before adding time.',
      'Activity type switches between non-project categories, regular assigned tasks, and requests/service requests.',
      'Normal and Afterhours hours are recorded separately.',
      'Entry details capture hours, required description/comment, and work-location information.',
      'Save draft persists editable entries without submitting them.',
      'Save week submits eligible time into the approval workflow.',
      'Personal defaults can automatically present preferred non-project categories or task rows.',
      'Submitted or approved days follow daily locking and correction rules.'
    ],
    steps: [
      'Choose the correct week.',
      'Select or add an activity.',
      'Open an entry for the correct day and time type.',
      'Enter hours and a reportable description.',
      'Save the draft and verify the status.',
      'Submit the required days or week when complete.'
    ],
    statuses: ['Draft', 'Submitted', 'Manager declined / Correction', 'Manager approved', 'PM approved', 'Accounting ready', 'Reconciled', 'Locked'],
    notes: [
      'Vacation is used for PTO; Holiday is reserved for company-paid holidays and the floating holiday.',
      'All required descriptions must be present before saving or submitting time.',
      'The current multiview design uses direct entry, guided entry, and personal defaults rather than a legacy duplication shortcut.'
    ]
  },
  'manager-approval': {
    category: 'Time & Approvals',
    audience: ['Manager', 'Administrator'],
    purpose: 'Review submitted time and either approve it or return it for correction.',
    functions: [
      'Shows submitted time awaiting review.',
      'Displays employee, date, project/task, time type, hours, and description context.',
      'Approve advances eligible time in the workflow.',
      'Decline returns time for correction with accountable status history.',
      'Pending indicators identify actionable approval work.',
      'Local administrator password-reset approvals may also appear for authorized users.'
    ],
    steps: [
      'Open Approval Inbox.',
      'Review the employee’s entries and supporting descriptions.',
      'Confirm project/task and Normal/Afterhours classification.',
      'Approve accurate entries or decline entries that require correction.',
      'Verify the pending count updates.'
    ],
    statuses: ['Pending', 'Approved', 'Declined / Correction'],
    notes: ['Approval should reflect actual work and policy, not only the total number of hours.']
  },
  utilization: {
    category: 'Time & Approvals',
    audience: ['Engineer', 'Manager', 'Project Manager', 'Administrator'],
    purpose: 'Measure eligible billable effort against the configured utilization target.',
    functions: [
      'Shows individual yearly and current-quarter utilization.',
      'Displays target percentage, target hours, current billable hours, and hours remaining.',
      'Manager views support team-level utilization review.',
      'Engineering team-lead views provide practice/team context.',
      'Uses approved or otherwise eligible time according to configured policy.'
    ],
    steps: [
      'Select Utilization.',
      'Review the period and target.',
      'Compare current eligible billable hours with target hours.',
      'Use detailed views to identify remaining hours or classification issues.'
    ],
    statuses: ['On target', 'Below target', 'Target met'],
    notes: ['Utilization is a reporting result; correcting source time or classification should occur in the appropriate workflow.']
  },
  'holiday-admin': {
    category: 'Time & Approvals',
    audience: ['Everyone', 'Administrator'],
    purpose: 'View company holidays and administer holiday calendars when authorized.',
    functions: [
      'Shows company holiday dates.',
      'Supports year selection.',
      'Authorized administrators can upload or maintain holiday data.',
      'Holiday data supports timesheet automation and reminder workflows.'
    ],
    steps: [
      'Select the relevant year.',
      'Review scheduled holidays.',
      'Administrators validate the upload format before applying changes.',
      'Confirm the holiday appears on the intended date.'
    ],
    notes: ['Holiday entries should not be used as a substitute for Vacation/PTO.']
  },
  'project-workload': {
    category: 'Projects & Resources',
    audience: ['Project Manager', 'Project Team Coordinator', 'Administrator'],
    purpose: 'Review project-manager workload, active and closed projects, and delivery risks.',
    functions: [
      'Lists projects assigned to a project manager.',
      'Separates active and closed work.',
      'Highlights workload and delivery-risk indicators.',
      'Supports navigation into project execution context.'
    ],
    steps: ['Open Project Workload.', 'Review assigned projects.', 'Investigate workload or delivery-risk indicators.', 'Open the relevant project workspace.']
  },
  'project-workspace': {
    category: 'Projects & Resources',
    audience: ['Engineer', 'Manager', 'Project Manager', 'Project Team Coordinator', 'Administrator'],
    purpose: 'Central execution workspace for projects, documents, assignments, tasks, stakeholders, and project status.',
    functions: [
      'Shows project identity, customer, status, contract/work type, project manager, and delivery context.',
      'Maintains engineering-visible documents and project artifacts.',
      'Shows assignments and resource requests.',
      'Supports task and work-item execution context.',
      'Provides project timeline and status information where available.',
      'Supports read-only Administrator View-As preview for effective-user validation.'
    ],
    steps: [
      'Search for or select the project.',
      'Review summary and status.',
      'Open documents, assignments, tasks, or requests.',
      'Complete role-authorized project actions.',
      'Confirm changes appear in the project history or current state.'
    ],
    statuses: ['Intake / Planned', 'Active', 'On hold', 'Complete / Closed', 'Archived'],
    notes: ['Project access can depend on role, assignment, and document visibility rules.']
  },
  'project-intake': {
    category: 'Projects & Resources',
    audience: ['Project Manager', 'Project Team Coordinator', 'Sales / Account Executive', 'Presales', 'Administrator'],
    purpose: 'Create and review the official request that begins a project or engineering-resource workflow.',
    functions: [
      'Captures customer, opportunity, contract, scope, dates, ownership, and requested-resource information.',
      'Tracks signed-date aging and intake readiness.',
      'Supports engineering resource demand and project handoff.',
      'Connects approved intake information to work-task and resource-assignment workflows.',
      'Preserves status and accountable updates.'
    ],
    steps: [
      'Create or open an intake.',
      'Complete customer, project, commercial, schedule, and resource fields.',
      'Attach or identify required documents.',
      'Submit or advance the intake according to the available workflow.',
      'Verify project and resource handoff readiness.'
    ],
    statuses: ['Draft', 'Submitted', 'Review', 'Approved', 'Rejected', 'Cancelled', 'Fulfilled'],
    notes: ['Do not create duplicate intakes for the same approved opportunity unless the business workflow requires separate projects.']
  },
  'sales-insights': {
    category: 'Sales & Opportunities',
    audience: ['Sales / Account Executive', 'Presales', 'Project Team Coordinator', 'Administrator'],
    purpose: 'Review sold-project handoff health and identify launch blockers.',
    functions: [
      'Shows sold-project handoff status.',
      'Identifies missing documents.',
      'Shows PM assignment and engineering-assignment readiness.',
      'Highlights launch blockers and aging.'
    ],
    steps: ['Open Sales Insights.', 'Filter or locate the sold work.', 'Review missing handoff elements.', 'Coordinate corrections with Sales, PM, PTC, or Engineering.']
  },
  opportunities: {
    category: 'Sales & Opportunities',
    audience: ['Sales / Account Executive', 'Presales', 'Engineer', 'Administrator'],
    purpose: 'Create and track opportunities and shared Sales, Presales, and Engineering actions.',
    functions: [
      'Active Pipeline lists open opportunities.',
      'Closed History lists closed opportunities.',
      'Search finds opportunities by topic, account, owner, or external ID.',
      'Add opportunity captures topic, customer/account, owner, external ID, source, estimated revenue, active date, and notes.',
      'Opportunity details show creator, updater, dates, status, revenue, and notes.',
      'Shared tasks capture task title, description, role, assigned user, due date, and status.',
      'Mark completed records who completed the task and when.',
      'Reopen returns a completed task to Open.',
      'Close opportunity records outcome and closed date.',
      'Reopen opportunity returns closed work to Active.',
      'Activity history preserves opportunity and task events.'
    ],
    steps: [
      'Select Add opportunity.',
      'Enter the required opportunity information and save.',
      'Open the new opportunity.',
      'Add Sales, Presales, or Engineering tasks.',
      'Complete or reopen tasks as work progresses.',
      'Close the opportunity with the correct outcome and date.'
    ],
    statuses: ['Active', 'Closed — Won', 'Closed — Lost', 'Closed — Cancelled', 'Closed — Other', 'Task Open', 'Task Completed', 'Task Cancelled'],
    notes: [
      'The original spreadsheet was used only as a field-format reference; its business records were not imported.',
      'Creator, updater, completer, and timestamps support accountability.'
    ]
  },
  'calendar-capacity': {
    category: 'Projects & Resources',
    audience: ['Engineer', 'Manager', 'Project Manager', 'Project Team Coordinator', 'Administrator'],
    purpose: 'Review individual, team, and department schedules and available capacity.',
    functions: [
      'Provides day, workweek, week, month, agenda, timeline, and future-month views.',
      'Shows assignments, time-off or holiday context, and capacity information where available.',
      'Supports individual, team, and department perspectives.',
      'Helps identify scheduling conflicts and open capacity.'
    ],
    steps: ['Choose the calendar scope.', 'Select the date range and view.', 'Review assignments and capacity.', 'Use the resource workflow to make authorized changes.']
  },
  'work-task-builder': {
    category: 'Projects & Resources',
    audience: ['Project Manager', 'Project Team Coordinator', 'Administrator'],
    purpose: 'Create and organize work tasks that can be assigned and used for time entry.',
    functions: [
      'Creates project tasks and work items.',
      'Supports task names, codes, classifications, estimated or assigned hours, and assignment context.',
      'Connects project/intake handoff to resource assignment.',
      'Makes eligible assigned tasks available on timesheets.'
    ],
    steps: ['Select the project or intake.', 'Create the required task structure.', 'Classify and estimate the work.', 'Assign resources through the approved workflow.', 'Verify the task appears for the assigned user.']
  },
  'customer-directory': {
    category: 'Customers & Commercial',
    audience: ['Project Team Coordinator', 'Sales / Account Executive', 'Accounting', 'Administrator'],
    purpose: 'Maintain customer/account records and contacts used across intake, projects, cost, billing, and reconciliation.',
    functions: [
      'Searches and lists customer records.',
      'Creates and updates authorized customer information.',
      'Maintains contacts and customer context.',
      'Supplies customer identity to opportunity, intake, project, contract, cost, and billing workflows.'
    ],
    steps: ['Search before creating a new customer.', 'Open the existing record or create a new one.', 'Complete required customer and contact fields.', 'Save and verify the record is available to related modules.'],
    notes: ['Avoid duplicate customer records with spelling variations.']
  },
  'work-register': {
    category: 'Customers & Commercial',
    audience: ['Engineer', 'Project Manager', 'Project Team Coordinator', 'Sales / Account Executive', 'Administrator'],
    purpose: 'Search active, closed, archived, and historical work across customers and delivery artifacts.',
    functions: [
      'Searches customers, projects, intakes, stakeholders, tasks, documents, hours, and cost indicators.',
      'Filters work by lifecycle status.',
      'Shows consolidated work detail.',
      'Supports controlled lifecycle updates where authorized.',
      'Preserves historical records rather than deleting delivery history.'
    ],
    steps: ['Enter a customer, project, number, stakeholder, or status.', 'Apply lifecycle filters.', 'Open the matching work item.', 'Review or perform authorized lifecycle actions.']
  },
  'rate-card-administration': {
    category: 'Customers & Commercial',
    audience: ['Project Team Coordinator', 'Accounting', 'Administrator'],
    purpose: 'Manage standard and customer-specific billing rates with accountable history.',
    functions: [
      'Maintains standard, customer-specific, Toyota, Hyundai, service-request, emergency, travel, and imported rates.',
      'Supports effective dates and applicable work types.',
      'Provides audit history for rate changes.',
      'Supplies rates to billing and financial calculations.'
    ],
    steps: ['Locate the relevant rate card.', 'Review customer/work-type/effective-date scope.', 'Add or update the authorized rate.', 'Save and verify audit history.'],
    notes: ['Rate changes can affect billing; validate effective dates and approval before saving.']
  },
  contracts: {
    category: 'Customers & Commercial',
    audience: ['Sales / Account Executive', 'Project Team Coordinator', 'Accounting', 'Administrator'],
    purpose: 'Manage prepaid, block-of-hours, contract balance, expiration, and consumption information.',
    functions: [
      'Creates and maintains contract or prepaid-hour records.',
      'Tracks original hours, credits, consumption, remaining balance, and expiration.',
      'Supports work-consumption records.',
      'Provides weekly AE balance reporting.',
      'Shows contract status and customer relationship.'
    ],
    steps: ['Select the customer.', 'Create or open the contract.', 'Review hours, credits, dates, and status.', 'Record or verify consumption.', 'Review remaining balance and expiration risk.']
  },
  'cost-alerts': {
    category: 'Customers & Commercial',
    audience: ['Manager', 'Project Manager', 'Project Team Coordinator', 'Accounting', 'Administrator'],
    purpose: 'Identify planned-cost, assignment, and consumption risks before they become billing or delivery problems.',
    functions: [
      'Detects missing cost plans.',
      'Identifies over-assigned or over-consumed work.',
      'Routes alerts to PM, manager, and Project Team Coordinator according to configuration.',
      'Shows acknowledgment or operational context where implemented.'
    ],
    steps: ['Review active alerts.', 'Open the affected project.', 'Validate plan, assignment, and used hours.', 'Correct the source issue or document the approved exception.']
  },
  'time-compliance': {
    category: 'Time & Approvals',
    audience: ['Manager', 'Project Team Coordinator', 'Administrator'],
    purpose: 'Identify missing time and prepare reminders or escalations under configured rules.',
    functions: [
      'Finds users missing required weekly time.',
      'Supports weekly and month-end reminder logic.',
      'Uses holiday information for reminder timing.',
      'Shows templates, preview, history, and retry-prevention context where enabled.',
      'Supports safe test or preview behavior before live notification.'
    ],
    steps: ['Select the compliance period.', 'Review missing-time results.', 'Validate recipients and timing.', 'Preview the notification.', 'Send only through authorized controls.']
  },
  workflow: {
    category: 'Approvals & Accounting',
    audience: ['Manager', 'Project Manager', 'Accounting', 'Administrator'],
    purpose: 'Coordinate approval, accounting readiness, reconciliation, locking, export, and audit.',
    functions: [
      'Shows workflow state after time submission.',
      'Supports manager and project validation.',
      'Moves eligible records to accounting readiness.',
      'Supports reconciliation and locking.',
      'Prepares authorized Excel, PDF, or package exports.',
      'Preserves audit visibility for workflow actions.'
    ],
    steps: ['Select the period or work item.', 'Review its current state.', 'Complete the action assigned to your role.', 'Verify the next state and audit record.'],
    statuses: ['Submitted', 'Manager approved', 'PM approved', 'Accounting ready', 'Reconciled', 'Locked']
  },
  'billing-readiness': {
    category: 'Approvals & Accounting',
    audience: ['Project Manager', 'Accounting', 'Administrator'],
    purpose: 'Determine whether project time, expenses, approvals, rates, and supporting information are ready for billing.',
    functions: [
      'Highlights missing approvals or billing data.',
      'Shows approved-but-not-ready records.',
      'Supports review before invoice preparation.',
      'Connects project closeout and accounting workflows.'
    ],
    steps: ['Select the billing period or project.', 'Review readiness blockers.', 'Correct missing approvals, rates, or evidence.', 'Confirm readiness before invoice preparation.']
  },
  'project-closeout': {
    category: 'Approvals & Accounting',
    audience: ['Project Manager', 'Project Team Coordinator', 'Accounting', 'Administrator'],
    purpose: 'Close project delivery in a controlled manner after work, billing, and documentation are complete.',
    functions: [
      'Reviews completion prerequisites.',
      'Checks remaining tasks, hours, documents, billing, and exceptions.',
      'Records closeout status and accountable actions.',
      'Preserves closed-project history.'
    ],
    steps: ['Open the project closeout record.', 'Review every prerequisite.', 'Resolve blockers or document approved exceptions.', 'Complete the authorized closeout action.']
  },
  'closeout-email': {
    category: 'Approvals & Accounting',
    audience: ['Project Manager', 'Project Team Coordinator', 'Administrator'],
    purpose: 'Prepare or send approved project-closeout communications.',
    functions: [
      'Builds closeout email content from project context.',
      'Supports preview before sending.',
      'Records send status and relevant history where enabled.'
    ],
    steps: ['Open the completed project.', 'Review recipients and message content.', 'Preview the email.', 'Send only after closeout approval.']
  },
  'invoice-billing-center': {
    category: 'Approvals & Accounting',
    audience: ['Accounting', 'Project Manager', 'Administrator'],
    purpose: 'Prepare partial and final invoice packages with detailed customer-facing evidence.',
    functions: [
      'Lists billing-ready and recently closed projects.',
      'Prepares partial or final invoice packages.',
      'Shows time, rates, customer, project, and billing evidence.',
      'Supports invoice header customization.',
      'Provides Over / Under and T&M balance reporting.',
      'Supports preview and authorized export.'
    ],
    steps: ['Select the customer/project and billing period.', 'Review approved time, rates, expenses, and balances.', 'Choose partial or final invoice.', 'Review the preview.', 'Export or advance through the accounting workflow.']
  },
  'certify-integration': {
    category: 'Expenses & Integrations',
    audience: ['Accounting', 'Project Manager', 'Administrator'],
    purpose: 'Review and manage the Certinia/Certify delivery and expense-integration foundation.',
    functions: [
      'Shows integration configuration and readiness.',
      'Supports controlled import or staging workflows where implemented.',
      'Displays row-level validation or error context.',
      'Preserves audit and source identifiers.'
    ],
    steps: ['Review integration status.', 'Select the approved import/staging action.', 'Validate source and mapping.', 'Review errors before committing records.'],
    notes: ['Do not treat a preview or staged row as a completed financial posting.']
  },
  'crm-integration': {
    category: 'Expenses & Integrations',
    audience: ['Sales / Account Executive', 'Project Team Coordinator', 'Administrator'],
    purpose: 'Connect SELL, Salesforce, Certinia, ServiceNow, and approved custom CRM/ERP platforms and review sanitized service availability.',
    functions: [
      'Shows built-in and manually registered CRM/ERP providers.',
      'Supports OAuth 2.0 or write-only API-key configuration.',
      'Encrypts credentials server-side and never returns their values.',
      'Runs explicit public-HTTPS availability tests and records sanitized status.',
      'Audits provider, credential, OAuth, and connection-test actions.'
    ],
    steps: ['Select a provider.', 'Save its non-secret endpoint and authentication metadata.', 'Save the write-only credential.', 'Complete OAuth consent when required.', 'Run Test availability and review the resulting status.'],
    notes: ['Only Integration Administrators and Administrators can change connection settings. View-As remains read-only.']
  },
  'psa-modules': {
    category: 'Expenses & Integrations',
    audience: ['Accounting', 'Project Manager', 'Administrator'],
    purpose: 'Provide connected PSA workflow summaries for expense, invoice, project, and billing readiness.',
    functions: [
      'Shows available PSA summary data.',
      'Links related project, expense, invoice, and billing-readiness workflows.',
      'Indicates unavailable or not-yet-connected data safely.'
    ],
    steps: ['Open PSA Modules.', 'Review the connected summaries.', 'Open the relevant detailed workflow.']
  },
  reporting: {
    category: 'Reporting',
    audience: ['Engineer', 'Manager', 'Project Manager', 'Project Team Coordinator', 'Accounting', 'Administrator'],
    purpose: 'Analyze operational, accountability, utilization, project, and financial information.',
    functions: [
      'Filters reports by customer, project, PM, engineer, team, contract type, and period where supported.',
      'Provides accountability and missing/late-time reporting.',
      'Provides project and financial reporting.',
      'Supports authorized exports.',
      'Uses source workflow data and role-based visibility.'
    ],
    steps: ['Choose the report.', 'Set the period and filters.', 'Review totals and detail.', 'Export only when needed and authorized.']
  },
  'audit-history': {
    category: 'Security & Access',
    audience: ['Administrator'],
    purpose: 'Review security, administrative, approval, export, notification, and system events.',
    functions: [
      'Filters audit events.',
      'Shows actor, action, target, timestamp, and supporting details where recorded.',
      'Supports accountability and troubleshooting.',
      'Provides evidence without allowing historical records to be silently rewritten.'
    ],
    steps: ['Select the date range and event type.', 'Search for the actor or target.', 'Review event details.', 'Preserve relevant evidence for investigation.']
  },
  'user-admin': {
    category: 'Administration',
    audience: ['Administrator'],
    purpose: 'Manage ProjectPulse users and account state.',
    functions: [
      'Lists users and identity information.',
      'Creates or updates supported local user records.',
      'Enables or disables authorized accounts.',
      'Supports password or onboarding-related administrative workflows where implemented.'
    ],
    steps: ['Search for the user first.', 'Open the user record.', 'Update only approved fields.', 'Save and verify effective access.']
  },
  'azure-admin': {
    category: 'Administration',
    audience: ['Administrator'],
    purpose: 'Configure and review Microsoft Entra directory integration and user-import readiness.',
    functions: [
      'Shows tenant and application configuration.',
      'Previews directory users under configured filters.',
      'Supports controlled user import or synchronization.',
      'Shows import runs, errors, and source-provider context.',
      'Protects client secrets from normal page display.'
    ],
    steps: ['Review tenant profile and configuration.', 'Preview users.', 'Apply filters and check existing-user matches.', 'Run an approved import or synchronization.', 'Review results and errors.'],
    notes: ['Changing Entra configuration affects identity workflows and requires controlled validation.']
  },
  'role-admin': {
    category: 'Administration',
    audience: ['Administrator'],
    purpose: 'Assign and review user roles.',
    functions: [
      'Searches the user directory.',
      'Shows current role assignments.',
      'Adds or removes authorized roles.',
      'Supports effective-access validation after changes.'
    ],
    steps: ['Locate the user.', 'Review current roles.', 'Apply the approved role change.', 'Save and verify the user’s effective workspace.']
  },
  'roles-permissions-matrix': {
    category: 'Administration',
    audience: ['Administrator'],
    purpose: 'Review how roles map to permissions and module actions.',
    functions: [
      'Displays roles and permission codes.',
      'Helps identify why a module or action is visible or hidden.',
      'Supports governance and least-privilege review.'
    ],
    steps: ['Select the role.', 'Review assigned permissions.', 'Compare with required job duties.', 'Use approved role administration to make changes.']
  },
  'service-control': {
    category: 'Platform Operations',
    audience: ['Administrator'],
    purpose: 'Review platform service, API, version, log, and controlled restart information.',
    functions: [
      'Shows service and API health.',
      'Displays version inventory and recent operational information.',
      'Provides controlled restart actions only where explicitly supported.',
      'Preserves operational evidence.'
    ],
    steps: ['Review health before taking action.', 'Inspect recent logs or version information.', 'Use restart only after confirming scope and impact.', 'Validate health after the action.']
  },
  'backup-dr': {
    category: 'Platform Operations',
    audience: ['Administrator'],
    purpose: 'Create and validate ProjectPulse backup and disaster-recovery bundles.',
    functions: [
      'Shows backup readiness.',
      'Creates controlled backup bundles.',
      'Records backup metadata and validation results.',
      'Supports disaster-recovery preparedness without automatically restoring production.'
    ],
    steps: ['Review current service and storage readiness.', 'Create an approved backup.', 'Verify completion and evidence.', 'Use Restore Validation before relying on the backup.']
  },
  'restore-validation': {
    category: 'Platform Operations',
    audience: ['Administrator'],
    purpose: 'Validate that a selected backup can be used without overwriting the active production system.',
    functions: [
      'Lists eligible restore points.',
      'Performs safe validation checks.',
      'Records evidence and failures.',
      'Does not silently replace production data.'
    ],
    steps: ['Select the approved restore point.', 'Run validation.', 'Review every validation result.', 'Escalate failures before declaring the backup usable.']
  },
  'backup-retention': {
    category: 'Platform Operations',
    audience: ['Administrator'],
    purpose: 'Review retention policy and safely remove eligible older backups.',
    functions: [
      'Shows backup age and retention classification.',
      'Protects required restore points.',
      'Supports controlled cleanup.',
      'Records retention decisions.'
    ],
    steps: ['Review policy and protected points.', 'Select only eligible backups.', 'Confirm cleanup scope.', 'Verify retained restore coverage after cleanup.']
  },
  'replication-sync': {
    category: 'Platform Operations',
    audience: ['Administrator'],
    purpose: 'Review replication, synchronization, backup freshness, and failover-readiness information.',
    functions: [
      'Shows database role and synchronization state.',
      'Shows backup freshness and peer configuration.',
      'Highlights replication or readiness problems.',
      'Supports investigation without initiating unapproved failover.'
    ],
    steps: ['Review current primary/peer state.', 'Check synchronization and freshness.', 'Investigate warnings.', 'Follow the approved failover or recovery runbook when required.']
  },
  'cicd-pipeline': {
    category: 'Platform Operations',
    audience: ['Administrator'],
    purpose: 'Review build and deployment pipeline readiness and evidence.',
    functions: [
      'Shows pipeline or deployment state where connected.',
      'Supports validation of immutable image and revision information.',
      'Provides operational context for controlled releases.'
    ],
    steps: ['Review source checkpoint.', 'Review build status and artifact identity.', 'Confirm deployment approval and rollback baseline.', 'Validate the resulting runtime.']
  }
};

const glossary = [
  ['Active route', 'The page identifier after # in the browser address, such as #timesheet.'],
  ['API', 'The backend service that validates requests and reads or changes database information.'],
  ['Afterhours', 'Time recorded separately from Normal time according to company and approval policy.'],
  ['Audit event', 'A recorded action with actor, timestamp, target, and supporting details.'],
  ['Effective user', 'The identity whose access is being evaluated, including Administrator View-As where supported.'],
  ['Immutable image', 'A deployed container image referenced by digest so its contents cannot silently change under the same reference.'],
  ['Module', 'A ProjectPulse page or workflow identified by a module number and route.'],
  ['Permission', 'A specific authorization code that controls visibility or an action.'],
  ['Role', 'A collection of responsibilities and permissions assigned to a user.'],
  ['Session', 'The time-limited authenticated browser context used to call protected ProjectPulse APIs.'],
  ['Worktree', 'A separate Git working directory used to isolate module development.']
];

function flattenText(value) {
  if (Array.isArray(value)) return value.map(flattenText).join(' ');
  if (value && typeof value === 'object') return Object.values(value).map(flattenText).join(' ');
  return String(value ?? '');
}

function moduleCode(module) {
  const label = String(module?.navLabel || '').trim();
  return label || 'Installed module';
}

function moduleAudience(module, detailed) {
  if (detailed?.audience?.length) return detailed.audience;

  const roleCodes = Array.isArray(module?.roleCodes) ? module.roleCodes : [];
  const permissions = Array.isArray(module?.permissions) ? module.permissions : [];

  if (!roleCodes.length && !permissions.length) return ['Everyone'];
  if (permissions.includes('SYSTEM_ADMINISTRATION') || permissions.includes('MANAGE_ALL')) {
    return ['Administrator'];
  }

  return ['Role-authorized users'];
}

function accessText(module) {
  const roleCodes = Array.isArray(module?.roleCodes) ? module.roleCodes : [];
  const permissions = Array.isArray(module?.permissions) ? module.permissions : [];

  if (!roleCodes.length && !permissions.length) {
    return 'Available to every authenticated ProjectPulse user.';
  }

  const parts = [];
  if (roleCodes.length) parts.push(`Roles: ${roleCodes.join(', ')}`);
  if (permissions.length) parts.push(`Permissions: ${permissions.join(', ')}`);
  return parts.join(' • ');
}

function genericGuide(module) {
  return {
    category: module?.group || 'Installed Modules',
    purpose: module?.description || 'Provides an installed ProjectPulse workflow.',
    functions: [
      `Opens the ${module?.title || module?.route || 'module'} page.`,
      'Loads role-authorized data from ProjectPulse APIs.',
      'Displays page-specific information and controls.',
      'Validates write actions on the backend before changing stored data.',
      'Preserves role and permission boundaries.'
    ],
    steps: [
      'Open the module from Dashboard or navigation.',
      'Review the page heading, status, filters, and available actions.',
      'Complete required fields before selecting a write action.',
      'Confirm the success message or updated status.'
    ],
    notes: [
      'The exact controls shown depend on current role, permission, record state, and available data.'
    ]
  };
}

export default function SystemUserGuide({ modules = [] }) {
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState('All categories');
  const [role, setRole] = useState('All roles');
  const [openAll, setOpenAll] = useState(false);

  const normalizedModules = useMemo(() => {
    const seen = new Set();

    return (Array.isArray(modules) ? modules : [])
      .filter((module) => module?.route && module.route !== 'user-guide')
      .filter((module) => {
        if (seen.has(module.route)) return false;
        seen.add(module.route);
        return true;
      })
      .map((module) => {
        const detailed = detailedModuleGuides[module.route] || genericGuide(module);
        return {
          kind: 'module',
          id: `module-${module.route}`,
          route: module.route,
          href: module.href || `#${module.route}`,
          code: moduleCode(module),
          title: module.title || module.route,
          summary: detailed.purpose || module.description,
          category: detailed.category || module.group || 'Installed Modules',
          audience: moduleAudience(module, detailed),
          access: accessText(module),
          functions: detailed.functions || [],
          steps: detailed.steps || [],
          statuses: detailed.statuses || [],
          notes: detailed.notes || [],
          sourceDescription: module.description || ''
        };
      })
      .sort(compareProjectPulseModules);
  }, [modules]);

  const allEntries = useMemo(() => {
    const globalEntries = globalGuide.map((entry) => ({
      ...entry,
      kind: 'global',
      code: 'Platform function',
      access: entry.audience.includes('Administrator')
        ? 'Audience-specific guidance; page and action access remain role controlled.'
        : 'Available to every authenticated ProjectPulse user.',
      statuses: entry.statuses || [],
      notes: entry.notes || []
    }));

    return [...globalEntries, ...normalizedModules];
  }, [normalizedModules]);

  const categories = useMemo(
    () => ['All categories', ...Array.from(new Set(allEntries.map((entry) => entry.category))).sort()],
    [allEntries]
  );

  const filteredEntries = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();

    return allEntries.filter((entry) => {
      if (category !== 'All categories' && entry.category !== category) return false;

      if (
        role !== 'All roles'
        && !entry.audience.includes('Everyone')
        && !entry.audience.includes(role)
      ) {
        return false;
      }

      if (!normalizedQuery) return true;

      return flattenText(entry).toLowerCase().includes(normalizedQuery);
    });
  }, [allEntries, category, query, role]);

  const grouped = useMemo(() => {
    const result = new Map();

    filteredEntries.forEach((entry) => {
      if (!result.has(entry.category)) result.set(entry.category, []);
      result.get(entry.category).push(entry);
    });

    return [...result.entries()];
  }, [filteredEntries]);

  return (
    <div className="system-user-guide" data-module="999" data-route="user-guide">
      <header className="system-user-guide-hero">
        <div>
          <p>MODULE 999</p>
          <h1>ProjectPulse Complete User Guide</h1>
          <span>
            Searchable documentation for every global function and every installed
            ProjectPulse module. The guide is visible to all authenticated users;
            the documented modules continue to enforce their own role and permission rules.
          </span>
        </div>
        <div className="system-user-guide-hero-actions">
          <span className="system-user-guide-availability">Available to everyone</span>
          <button type="button" onClick={() => window.print()}>Print guide</button>
        </div>
      </header>

      <section className="system-user-guide-principles" aria-label="Guide principles">
        <article>
          <strong>{normalizedModules.length}</strong>
          <span>Installed module routes documented</span>
        </article>
        <article>
          <strong>{globalGuide.length}</strong>
          <span>Global platform functions documented</span>
        </article>
        <article>
          <strong>Role aware</strong>
          <span>The guide is public internally; system actions remain protected</span>
        </article>
        <article>
          <strong>Living guide</strong>
          <span>New registry modules are automatically included</span>
        </article>
      </section>

      <section className="system-user-guide-controls" aria-label="Guide search and filters">
        <label className="system-user-guide-search">
          <span>Search the complete guide</span>
          <input
            type="search"
            value={query}
            placeholder="Search module number, page, button, status, task, billing, access..."
            onChange={(event) => setQuery(event.target.value)}
          />
        </label>

        <label>
          <span>Category</span>
          <select value={category} onChange={(event) => setCategory(event.target.value)}>
            {categories.map((option) => (
              <option value={option} key={option}>{option}</option>
            ))}
          </select>
        </label>

        <label>
          <span>Audience</span>
          <select value={role} onChange={(event) => setRole(event.target.value)}>
            {roleOptions.map((option) => (
              <option value={option} key={option}>{option}</option>
            ))}
          </select>
        </label>

        <div className="system-user-guide-control-actions">
          <button type="button" onClick={() => setOpenAll(true)}>Expand all</button>
          <button type="button" onClick={() => setOpenAll(false)}>Collapse all</button>
          <button
            type="button"
            onClick={() => {
              setQuery('');
              setCategory('All categories');
              setRole('All roles');
            }}
          >
            Reset
          </button>
        </div>
      </section>

      <div className="system-user-guide-result-summary">
        <strong>{filteredEntries.length}</strong>
        <span>matching guide entries</span>
      </div>

      {grouped.length ? (
        grouped.map(([groupName, entries]) => (
          <section className="system-user-guide-group" key={groupName}>
            <div className="system-user-guide-group-heading">
              <div>
                <p>Guide category</p>
                <h2>{groupName}</h2>
              </div>
              <span>{entries.length} entries</span>
            </div>

            <div className="system-user-guide-entry-list">
              {entries.map((entry) => (
                <details className="system-user-guide-entry" open={openAll || undefined} key={entry.id}>
                  <summary>
                    <div>
                      <span>{entry.code}</span>
                      <strong>{entry.title}</strong>
                      <small>{entry.summary}</small>
                    </div>
                    <em>{entry.kind === 'module' ? 'Module guide' : 'Platform guide'}</em>
                  </summary>

                  <div className="system-user-guide-entry-body">
                    <div className="system-user-guide-meta-grid">
                      <article>
                        <span>Audience</span>
                        <strong>{entry.audience.join(', ')}</strong>
                      </article>
                      <article>
                        <span>Access</span>
                        <strong>{entry.access}</strong>
                      </article>
                      {entry.route ? (
                        <article>
                          <span>Route</span>
                          <strong>#{entry.route}</strong>
                        </article>
                      ) : null}
                    </div>

                    <section>
                      <h3>Purpose</h3>
                      <p>{entry.summary}</p>
                    </section>

                    <section>
                      <h3>Functions and controls</h3>
                      <ol>
                        {entry.functions.map((item) => <li key={item}>{item}</li>)}
                      </ol>
                    </section>

                    <section>
                      <h3>How to use it</h3>
                      <ol>
                        {entry.steps.map((item) => <li key={item}>{item}</li>)}
                      </ol>
                    </section>

                    {entry.statuses.length ? (
                      <section>
                        <h3>Status meanings</h3>
                        <div className="system-user-guide-status-list">
                          {entry.statuses.map((status) => <span key={status}>{status}</span>)}
                        </div>
                      </section>
                    ) : null}

                    {entry.notes.length ? (
                      <section className="system-user-guide-notes">
                        <h3>Important notes</h3>
                        <ul>
                          {entry.notes.map((note) => <li key={note}>{note}</li>)}
                        </ul>
                      </section>
                    ) : null}

                    {entry.kind === 'module' ? (
                      <div className="system-user-guide-open-row">
                        <a href={entry.href}>Open {entry.title}</a>
                      </div>
                    ) : null}
                  </div>
                </details>
              ))}
            </div>
          </section>
        ))
      ) : (
        <section className="system-user-guide-empty">
          <h2>No guide entries match the current filters</h2>
          <p>Clear the search or reset Category and Audience.</p>
        </section>
      )}

      <section className="system-user-guide-glossary">
        <div className="system-user-guide-group-heading">
          <div>
            <p>Reference</p>
            <h2>ProjectPulse glossary</h2>
          </div>
          <span>{glossary.length} terms</span>
        </div>
        <dl>
          {glossary.map(([term, definition]) => (
            <div key={term}>
              <dt>{term}</dt>
              <dd>{definition}</dd>
            </div>
          ))}
        </dl>
      </section>

      <section className="system-user-guide-support">
        <h2>What to include in a support request</h2>
        <p>
          Provide the page route, module number, date and time, action attempted,
          exact error text, HTTP status from the Network panel when available,
          and whether the problem persists after one safe refresh. Never include
          passwords, session tokens, secrets, or unnecessary customer data.
        </p>
      </section>
    </div>
  );
}
