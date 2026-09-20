import { useEffect, useRef, useState } from 'react';

export default function ProjectFlowHiveDocumentReadiness({ projectId, getJson, onState }) {
  const [result, setResult] = useState(null);
  const [error, setError] = useState('');
  const [refresh, setRefresh] = useState(0);
  const publish = useRef(onState);
  publish.current = onState;
  useEffect(() => {
    const controller = new AbortController();
    let timer;
    setResult(null); setError(''); publish.current(null);
    async function read() {
      let delay = 30000;
      try {
        const value = await getJson(`/api/project-flowhive/projects/${projectId}/documents/readiness`, controller.signal);
        if (controller.signal.aborted || value.projectId !== projectId) return;
        setResult(value); setError(''); publish.current(value);
        if (value.preparation?.status === 'preparing') delay = 5000;
      } catch (failure) {
        if (controller.signal.aborted) return;
        setResult(null); publish.current(null);
        setError('Document readiness is temporarily unavailable. Your files and plan are saved. Try checking again.');
      }
      if (!controller.signal.aborted) timer = window.setTimeout(read, delay);
    }
    read();
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [projectId, getJson, refresh]);
  const preparation = result?.preparation;
  return <section className="flowhive-document-preparation" aria-label="Document readiness">
    <div role="status" aria-live="polite">
      <strong>{error ? 'Readiness unavailable' : preparation?.label || 'Checking documents…'}</strong>
      <p>{error || preparation?.message || 'Checking the current project documents before planning.'}</p>
      {preparation?.totalCount > 0 && <span>{preparation.readyCount} of {preparation.totalCount} current documents ready</span>}
    </div>
    <button type="button" onClick={() => setRefresh(value => value + 1)}>Check again</button>
    {preparation?.documents?.length > 0 && <details><summary>Document details</summary>
      <ul>{preparation.documents.map(document => <li key={document.documentId}>
        <strong>{document.category}: {document.fileName}</strong><span>{document.status}</span>
        {document.message && <p>{document.message}</p>}
      </li>)}</ul>
    </details>}
  </section>;
}
