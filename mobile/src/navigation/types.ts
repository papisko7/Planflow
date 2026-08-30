// Shared param list for the root bottom-tab navigator, kept next to the navigator that uses it.
export type RootTabParamList = {
  Dashboard: undefined;
  Tasks: undefined;
  Teams: undefined;
  Calendar: undefined;
  Settings: undefined;
};

// Top-level stack: the tab navigator is one screen, with detail screens pushed on top of it
// so they get native push/back behavior instead of living inside a tab.
export type RootStackParamList = {
  Tabs: undefined;
  TaskDetail: { taskId: string };
};
