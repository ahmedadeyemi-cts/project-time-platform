import { useEffect } from 'react';
import './critical-route-presentation.css';

const PREFIX = 'projectpulse-route-';

function currentRoute() {
  return window.location.hash.replace(/^#/, '').split('?')[0] || 'dashboard';
}

export default function CriticalRoutePresentationBoundary() {
  useEffect(() => {
    const apply = () => {
      for (const className of [...document.body.classList]) {
        if (className.startsWith(PREFIX)) document.body.classList.remove(className);
      }
      document.body.classList.add(`${PREFIX}${currentRoute()}`);
    };
    apply();
    window.addEventListener('hashchange', apply);
    return () => {
      window.removeEventListener('hashchange', apply);
      for (const className of [...document.body.classList]) {
        if (className.startsWith(PREFIX)) document.body.classList.remove(className);
      }
    };
  }, []);

  return null;
}
