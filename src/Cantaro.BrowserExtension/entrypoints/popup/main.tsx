import React from 'react';
import ReactDOM from 'react-dom/client';
import { ExtensionApp } from '../../app/shell/ExtensionApp';
import './style.css';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ExtensionApp />
  </React.StrictMode>,
);
