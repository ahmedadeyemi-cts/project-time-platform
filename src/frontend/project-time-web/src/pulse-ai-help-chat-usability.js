import './pulse-ai-help-chat-usability.css';

const PANEL_SELECTOR = '.help-panel.pulse-ai-help-panel';
const MESSAGE_SELECTOR = '.help-messages';
const TEXTAREA_SELECTOR = '.help-input-row textarea';
const WIRED_ATTRIBUTE = 'data-pulse-ai-help-usability-wired';
const observers = new WeakMap();

function nearBottom(element) {
  const remaining = element.scrollHeight - element.scrollTop - element.clientHeight;
  return remaining <= Math.max(120, element.clientHeight * 0.18);
}

function scrollToBottom(messages, behavior = 'smooth') {
  if (!(messages instanceof HTMLElement)) return;
  requestAnimationFrame(() => {
    messages.scrollTo({ top: messages.scrollHeight, behavior });
  });
}

function addKeyboardHint(form) {
  if (!(form instanceof HTMLFormElement)) return;
  if (form.querySelector('.pulse-ai-help-keyboard-hint')) return;

  const hint = document.createElement('div');
  hint.className = 'pulse-ai-help-keyboard-hint';
  hint.setAttribute('aria-hidden', 'true');
  hint.textContent = 'Enter sends • Shift+Enter adds a line';
  form.append(hint);
}

function wirePanel(panel) {
  if (!(panel instanceof HTMLElement)) return;
  if (panel.getAttribute(WIRED_ATTRIBUTE) === 'true') return;
  panel.setAttribute(WIRED_ATTRIBUTE, 'true');

  const messages = panel.querySelector(MESSAGE_SELECTOR);
  const textarea = panel.querySelector(TEXTAREA_SELECTOR);
  const form = textarea?.closest('form');

  if (messages instanceof HTMLElement) {
    messages.setAttribute('role', 'log');
    messages.setAttribute('aria-live', 'polite');
    messages.setAttribute('aria-relevant', 'additions text');
    messages.setAttribute('tabindex', '0');
    messages.setAttribute('aria-label', 'Pulse AI conversation');

    let shouldFollowConversation = true;
    const updateFollowState = () => {
      shouldFollowConversation = nearBottom(messages);
    };
    messages.addEventListener('scroll', updateFollowState, { passive: true });
    messages.addEventListener('wheel', updateFollowState, { passive: true });
    messages.addEventListener('touchmove', updateFollowState, { passive: true });

    const observer = new MutationObserver((mutations) => {
      const userMessageAdded = mutations.some((mutation) =>
        [...mutation.addedNodes].some((node) =>
          node instanceof HTMLElement && node.matches('.help-message.user'))
      );
      const loadingOrAnswerChanged = mutations.some((mutation) =>
        mutation.type === 'childList' || mutation.type === 'characterData'
      );

      if (userMessageAdded || (shouldFollowConversation && loadingOrAnswerChanged)) {
        scrollToBottom(messages, userMessageAdded ? 'smooth' : 'auto');
      }
    });
    observer.observe(messages, { childList: true, subtree: true, characterData: true });
    observers.set(panel, observer);
    scrollToBottom(messages, 'auto');
  }

  if (textarea instanceof HTMLTextAreaElement) {
    textarea.setAttribute('aria-keyshortcuts', 'Enter Shift+Enter');
    textarea.setAttribute('enterkeyhint', 'send');
    requestAnimationFrame(() => textarea.focus({ preventScroll: true }));
  }

  addKeyboardHint(form);
}

function wireOpenPanels(root = document) {
  root.querySelectorAll?.(PANEL_SELECTOR).forEach(wirePanel);
}

function handleKeyDown(event) {
  const textarea = event.target;
  if (!(textarea instanceof HTMLTextAreaElement)) return;
  if (!textarea.matches(TEXTAREA_SELECTOR)) return;

  if (event.key === 'Escape') {
    const panel = textarea.closest(PANEL_SELECTOR);
    const close = panel?.querySelector('.celar-ai-chat-close, .help-header button[aria-label="Close Celar AI"], .help-header button[aria-label="Close help assistant"]');
    if (close instanceof HTMLButtonElement) {
      event.preventDefault();
      close.click();
    }
    return;
  }

  if (event.key !== 'Enter' || event.shiftKey || event.isComposing) return;
  if (event.ctrlKey || event.metaKey || event.altKey) return;

  const form = textarea.closest('form');
  if (!(form instanceof HTMLFormElement)) return;
  if (textarea.disabled || !textarea.value.trim()) return;

  event.preventDefault();
  event.stopPropagation();
  form.requestSubmit();
}

function cleanupRemovedPanels(nodes) {
  nodes.forEach((node) => {
    if (!(node instanceof HTMLElement)) return;
    const panels = node.matches(PANEL_SELECTOR)
      ? [node]
      : [...node.querySelectorAll(PANEL_SELECTOR)];
    panels.forEach((panel) => {
      observers.get(panel)?.disconnect();
      observers.delete(panel);
    });
  });
}

document.addEventListener('keydown', handleKeyDown, true);

const rootObserver = new MutationObserver((mutations) => {
  mutations.forEach((mutation) => {
    mutation.addedNodes.forEach((node) => {
      if (!(node instanceof HTMLElement)) return;
      if (node.matches(PANEL_SELECTOR)) wirePanel(node);
      wireOpenPanels(node);
    });
    cleanupRemovedPanels([...mutation.removedNodes]);
  });
});

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    wireOpenPanels();
    rootObserver.observe(document.body, { childList: true, subtree: true });
  }, { once: true });
} else {
  wireOpenPanels();
  rootObserver.observe(document.body, { childList: true, subtree: true });
}

window.ProjectPulseHelpChatUsability = Object.freeze({
  contract: 'pulse-ai-help-chat-usability-v1',
  enterSends: true,
  shiftEnterAddsLine: true,
  conversationScrollOwned: true,
  externalProviderCalled: false
});
