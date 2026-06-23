import { useMemo, useState } from 'react';
import './help.css';

const helpTopics = [
  {
    keywords: ['timesheet', 'time', 'hours', 'normal', 'afterhours', 'ot', 'overtime'],
    answer:
      'On the Timesheet page, select a 0.00 cell for the activity and day you want to update. A time-entry window opens where you can enter hours, add a reportable comment, and choose work location details. Normal time and afterhours are tracked separately.'
  },
  {
    keywords: ['save', 'draft', 'refresh', 'lost', 'missing', 'not showing'],
    answer:
      'Use Save draft before leaving the page. A saved draft should reload after refresh. If submitted time does not reload, the API or database persistence needs to be checked with the timesheet validation commands.'
  },
  {
    keywords: ['submit', 'approval', 'manager', 'approve', 'reject', 'decline'],
    answer:
      'Submit sends the weekly timesheet for manager approval. After submission, the timesheet should lock until a manager approves it or returns it for correction. The manager approval screen is planned as the next workflow phase.'
  },
  {
    keywords: ['non-project', 'administrative', 'vacation', 'holiday', 'training', 'sick', 'peer support', 'fmla'],
    answer:
      'Non-project time includes categories such as Administrative, Peer Support, Vacation, Holiday, Sick Leave, Training, Bereavement, Jury Duty, FMLA, and disability-related categories. These categories support utilization and approval rules.'
  },
  {
    keywords: ['project', 'task', 'assignment', 'customer', 'contract'],
    answer:
      'Project time will be entered against assigned project tasks, not just the project itself. The project-task assignment workflow is planned after persistence and approval foundations are validated.'
  },
  {
    keywords: ['location', 'work location', 'timezone', 'engineer', 'resource profile'],
    answer:
      'Work location should eventually default from each engineer\'s resource profile. During onboarding, each engineer will need a work location group, work location, time zone, team/workgroup, manager, role, and project-task assignments.'
  },
  {
    keywords: ['utilization', 'target', '70', 'billable', 'pto', 'vacation'],
    answer:
      'Utilization will compare eligible billable and approved time against target thresholds. The platform currently has utilization policy data loaded, and the detailed calculation rules will be connected after time entry and approvals are persistent.'
  },
  {
    keywords: ['dark', 'light', 'theme', 'mode'],
    answer: 'Use the Dark mode or Light mode button in the top navigation to switch the display theme.'
  }
];

function findAnswer(question) {
  const normalized = question.trim().toLowerCase();
  if (!normalized) return 'Ask a question about the current page, timesheets, approvals, utilization, project tasks, or work locations.';

  const matchedTopic = helpTopics.find((topic) => topic.keywords.some((keyword) => normalized.includes(keyword)));

  if (matchedTopic) return matchedTopic.answer;

  return 'I can help with timesheets, saving drafts, submitting for approval, non-project time, project-task assignments, work locations, utilization, and light/dark mode. Try asking something like: How do I save my timesheet?';
}

export default function HelpAssistant() {
  const [isOpen, setIsOpen] = useState(false);
  const [question, setQuestion] = useState('');
  const [messages, setMessages] = useState([
    {
      role: 'assistant',
      text: 'Hi, I can help with this Project Time Platform page. Ask about time entry, saving drafts, submitting, work locations, utilization, or approvals.'
    }
  ]);

  const suggestions = useMemo(
    () => [
      'How do I save my timesheet?',
      'What is afterhours time?',
      'How does work location work?',
      'What happens after I submit?'
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

  return (
    <>
      <button className="help-launcher" type="button" onClick={() => setIsOpen(true)}>
        Help
      </button>

      {isOpen ? (
        <aside className="help-panel" aria-label="Page help assistant">
          <div className="help-header">
            <div>
              <strong>Page Help</strong>
              <span>Ask about the current workflow</span>
            </div>
            <button type="button" onClick={() => setIsOpen(false)} aria-label="Close help assistant">
              ×
            </button>
          </div>

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
              placeholder="Ask a help question..."
              onChange={(event) => setQuestion(event.target.value)}
            />
            <button type="submit">Send</button>
          </form>
        </aside>
      ) : null}
    </>
  );
}
