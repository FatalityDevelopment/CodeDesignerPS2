import './api/buffer-shim';
import { createRoot } from 'react-dom/client';
import { AppShell } from './AppShell';
import { startBridge } from './api/bridge';
import { startGameContext } from './shared/gameContext';
import { disassembler } from './pages/disassembler/disassembler-logic';
import './styles/theme.css';
import './styles/shared.css';
import './styles/chrome.css';
import './styles/dialog.css';
import './styles/shell.css';
import './styles/cd-app.css';

startGameContext();
disassembler.start();
startBridge();

createRoot(document.getElementById('root')!).render(<AppShell />);
