import { useEffect } from 'react';
import { Workbench } from './components/layout/Workbench';
import { useAuthStore } from './stores/authStore';
import { useSettingsStore } from './stores/settingsStore';
import { DialogHost } from './components/features/dialogs/DialogHost';

export function App() {
  const hydrateAuth = useAuthStore((s) => s.hydrate);
  const hydrateSettings = useSettingsStore((s) => s.hydrate);

  useEffect(() => {
    void hydrateAuth();
    void hydrateSettings();
  }, [hydrateAuth, hydrateSettings]);

  return (
    <>
      <Workbench />
      <DialogHost />
    </>
  );
}
