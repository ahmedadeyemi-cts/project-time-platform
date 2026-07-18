import { useMemo, useState } from 'react';
import './help.css';
import './help-assistant.css';

const helpTopics = [
  {
    keywords: ['guide', 'help', 'manual', 'documentation', 'module 999', '999'],
    answer:
      'Open Module 999 for the complete ProjectPulse user guide. It documents global functions, every installed module route, role expectations, step-by-step procedures, statuses, and troubleshooting.'
  },
  {
    keywords: ['timesheet', 'time', 'hours', 'normal', 'afterhours', 'ot', 'overtime'],
    answer:
      'Module 001 supports Weekly Grid, Daily Focus, Guided Add, Quick Entry List, and Smart Work Log. Enter hours against the correct project task, request, or non-project category, include the required description, save the draft, and submit eligible time when complete.'
  },
  {
    keywords: ['save', 'draft', 'refresh', 'lost', 'missing', 'not showing'],
    answer:
      'Wait for the save to complete before refreshing or leaving the page. A successful save persists data through the API. Refreshing should reload persisted records, but unsaved browser changes may be lost.'
  },
  {
    keywords: ['submit', 'approval', 'manager', 'approve', 'reject', 'decline'],
    answer:
      'Submitted time moves to Approval Inbox. Managers review the detail and approve accurate time or decline it for correction. Later workflow states can include PM approval, accounting readiness, reconciliation, and locking.'
  },
  {
    keywords: ['opportunity', 'sales', 'presales', 'pipeline', 'won', 'lost'],
    answer:
      'Module 063 tracks active and closed opportunities, owners, estimated and actual revenue, shared Sales/Presales/Engineering tasks, completion accountability, and activity history.'
  },
  {
    keywords: ['contract', 'prepaid', 'block of hours', 'balance', 'expiration'],
    answer:
      'Module 060 manages prepaid and block-of-hours records, credits, consumption, remaining balance, expiration, and weekly Account Executive balance reporting.'
  },
  {
    keywords: ['project', 'task', 'assignment', 'customer', 'intake'],
    answer:
      'Project Intake begins the delivery workflow. Project Workspace contains project context, documents, assignments, resource requests, and execution information. Work Task Builder creates eligible project tasks for assignment and time entry.'
  },
  {
    keywords: ['location', 'work location', 'timezone', 'resource profile'],
    answer:
      'Work-location information supports time-entry and resource context. Select the correct work-location values in entry details when required; administrators maintain user and directory information through authorized administration workflows.'
  },
  {
    keywords: ['utilization', 'target', 'billable', 'pto', 'vacation'],
    answer:
      'Utilization compares eligible billable time with configured targets. Review target hours, current eligible billable hours, utilization percentage, and hours remaining in Module 003.'
  },
  {
    keywords: ['access', 'permission', 'role', '403', 'denied'],
    answer:
      'ProjectPulse uses roles and permission codes. Module 999 is visible to everyone, but other modules and actions remain protected. HTTP 403 means the effective user is not authorized for that action.'
  },
  {
    keywords: ['dark', 'light', 'theme', 'mode'],
    answer:
      'Use the appearance control in the top navigation or profile settings to switch between light and dark mode.'
  }
];

function findAnswer(question) {
  const normalized = question.trim().toLowerCase();

  if (!normalized) {
    return 'Ask a question or open Module 999 for the complete ProjectPulse user guide.';
  }

  const matchedTopic = helpTopics.find((topic) =>
    topic.keywords.some((keyword) => normalized.includes(keyword))
  );

  if (matchedTopic) return matchedTopic.answer;

  return 'The complete answer may be in Module 999. Search the guide by module number, page, button, status, role, or business term.';
}

export default function HelpAssistant() {
  const [isOpen, setIsOpen] = useState(false);
  const [question, setQuestion] = useState('');
  const [messages, setMessages] = useState([
    {
      role: 'assistant',
      text: 'Hi. Ask a quick ProjectPulse question, or open Module 999 for the complete user guide.'
    }
  ]);

  const suggestions = useMemo(
    () => [
      'How do I use Module 999?',
      'How do I save my timesheet?',
      'How do approvals work?',
      'How do opportunities work?'
    ],
    []
  );

  function submitQuestion(nextQuestion = question) {
    const cleanQuestion = nextQuestion.trim();
    if (!cleanQuestion) return;

    setMessages((current) => [
      ...current,
      { role: 'user', text: cleanQuestion },
      { role: 'assistant', text: findAnswer(cleanQuestion) }
    ]);
    setQuestion('');
  }

  function openCompleteGuide() {
    setIsOpen(false);
    window.location.hash = 'user-guide';
  }

  return (
    <>
      <button className="help-launcher" type="button" onClick={() => setIsOpen(true)}>
        Help
      </button>

      {isOpen ? (
        <aside className="help-panel" aria-label="ProjectPulse help assistant">
          <div className="help-header">
            <div>
              <strong>ProjectPulse Help</strong>
              <span>Quick answers and complete documentation</span>
            </div>
            <button type="button" onClick={() => setIsOpen(false)} aria-label="Close help assistant">
              ×
            </button>
          </div>

          <button className="help-full-guide-button" type="button" onClick={openCompleteGuide}>
            Open Module 999 — Complete User Guide
          </button>

          <div className="help-messages">
            {messages.map((message, index) => (
              <div className={`help-message ${message.role}`} key={`${message.role}-${index}`}>
                {message.text}
              </div>
            ))}
          </div>

          <div className="help-suggestions" aria-label="Suggested help questions">
            {suggestions.map((suggestion) => (
              <button type="button" key={suggestion} onClick={() => submitQuestion(suggestion)}>
                {suggestion}
              </button>
            ))}
          </div>

          <form
            className="help-input-row"
            onSubmit={(event) => {
              event.preventDefault();
              submitQuestion();
            }}
          >
            <input
              value={question}
              placeholder="Ask ProjectPulse for help..."
              onChange={(event) => setQuestion(event.target.value)}
            />
            <button type="submit">Send</button>
          </form>
        </aside>
      ) : null}
    </>
  );
}
