import { create } from 'zustand';

export type ActivityView =
  | 'explorer'
  | 'debugger'
  | 'packets'
  | 'labels'
  | 'docs'
  | 'chat'
  | 'account'
  | 'settings';

export type BottomTab = 'debugger' | 'chat' | 'packets' | 'labels' | 'output';

interface LayoutState {
  activeView: ActivityView;
  setActiveView: (v: ActivityView) => void;

  sidebarVisible: boolean;
  toggleSidebar: () => void;
  sidebarWidth: number;
  setSidebarWidth: (w: number) => void;

  rightPanelVisible: boolean;
  toggleRightPanel: () => void;
  rightPanelWidth: number;
  setRightPanelWidth: (w: number) => void;

  bottomPanelVisible: boolean;
  toggleBottomPanel: () => void;
  bottomPanelHeight: number;
  setBottomPanelHeight: (h: number) => void;

  activeBottomTab: BottomTab;
  setActiveBottomTab: (t: BottomTab) => void;
}

export const useLayoutStore = create<LayoutState>((set) => ({
  activeView: 'explorer',
  setActiveView: (v) => set({ activeView: v, sidebarVisible: true }),

  sidebarVisible: true,
  toggleSidebar: () => set((s) => ({ sidebarVisible: !s.sidebarVisible })),
  sidebarWidth: 280,
  setSidebarWidth: (sidebarWidth) => set({ sidebarWidth: clamp(sidebarWidth, 180, 600) }),

  rightPanelVisible: false,
  toggleRightPanel: () => set((s) => ({ rightPanelVisible: !s.rightPanelVisible })),
  rightPanelWidth: 320,
  setRightPanelWidth: (rightPanelWidth) => set({ rightPanelWidth: clamp(rightPanelWidth, 200, 700) }),

  bottomPanelVisible: true,
  toggleBottomPanel: () => set((s) => ({ bottomPanelVisible: !s.bottomPanelVisible })),
  bottomPanelHeight: 240,
  setBottomPanelHeight: (bottomPanelHeight) =>
    set({ bottomPanelHeight: clamp(bottomPanelHeight, 80, 600) }),

  activeBottomTab: 'debugger',
  setActiveBottomTab: (activeBottomTab) => set({ activeBottomTab, bottomPanelVisible: true })
}));

function clamp(v: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, v));
}
