import { ModuleRegistry, AllCommunityModule } from 'ag-charts-community';

// AG Charts draws nothing until its modules are registered. Import this module wherever a chart is created.
ModuleRegistry.registerModules([AllCommunityModule]);
