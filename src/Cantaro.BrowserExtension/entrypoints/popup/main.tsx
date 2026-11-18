import React from 'react';
import ReactDOM from 'react-dom/client';
import { restorePopupTheme } from '../../app/settings/popupTheme';
import { ExtensionApp } from '../../app/shell/ExtensionApp';
import './style.css';

void restorePopupTheme().finally(() => {
  ReactDOM.createRoot(document.getElementById('root')!).render(
    <React.StrictMode>
      <ExtensionApp />
    </React.StrictMode>,
  );
});
