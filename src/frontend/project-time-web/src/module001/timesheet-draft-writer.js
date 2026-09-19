// Serialize draft writes so an older response cannot overtake a newer edit.
// The caller captures identity and week; queued writes are cancelled if that scope changes.
export function createDraftWriteQueue() {
  let tail = Promise.resolve();
  return {
    enqueue(write, isCurrent = () => true) {
      const task = tail.then(() => isCurrent() ? write() : false);
      tail = task.catch(() => false);
      return task;
    },
    idle() { return tail; }
  };
}
