// Mirrors src/Fullview.Domain/Entities/*.cs and SyncContext.cs exactly. `SyncJson.Options`
// on the API side is plain `JsonSerializerDefaults.Web` — camelCase properties, no
// [JsonStringEnumConverter] — so enums serialize as the integers below, in the same order
// as the C# enum declarations. Keep these numbers in lockstep with the C# source; there is
// no runtime check that catches drift between the two.

// Plain const objects rather than `enum` — tsconfig.app.json sets `erasableSyntaxOnly`
// (matches how Vite/esbuild strip types without a real TS enum transform), so this is the
// idiomatic replacement: same call-site shape (`SyncContext.Work`), still a numeric literal
// union as a type.

export const SyncContext = {
  Personal: 0,
  Work: 1,
} as const;
export type SyncContext = (typeof SyncContext)[keyof typeof SyncContext];

export const TodoPriority = {
  Focus: 0,
  Normal: 1,
  Someday: 2,
} as const;
export type TodoPriority = (typeof TodoPriority)[keyof typeof TodoPriority];

export const TodoEnergy = {
  QuickWin: 0,
  Deep: 1,
} as const;
export type TodoEnergy = (typeof TodoEnergy)[keyof typeof TodoEnergy];

export const MealSlot = {
  Breakfast: 0,
  Dinner: 1,
} as const;
export type MealSlot = (typeof MealSlot)[keyof typeof MealSlot];

export const RoutineType = {
  MorningPersonal: 0,
  EveningPersonal: 1,
  WorkStartup: 2,
  WorkShutdown: 3,
} as const;
export type RoutineType = (typeof RoutineType)[keyof typeof RoutineType];

export const AgendaEventSource = {
  Native: 0,
  GoogleCalendar: 1,
} as const;
export type AgendaEventSource = (typeof AgendaEventSource)[keyof typeof AgendaEventSource];

export const InboxPageState = {
  Queued: 0,
  Processed: 1,
  Filed: 2,
} as const;
export type InboxPageState = (typeof InboxPageState)[keyof typeof InboxPageState];

/** Common fields carried by every entity (matches abstract SyncEntity). */
interface SyncEntityBase {
  id: string;
  context: SyncContext;
  /** ISO 8601 with offset — .NET DateTimeOffset round-trips to this shape by default. */
  updatedAt: string;
  updatedBy: string;
  deleted: boolean;
}

export interface Todo extends SyncEntityBase {
  entityType: "Todo";
  title: string;
  priority: TodoPriority;
  dueDate: string | null;
  energy: TodoEnergy | null;
  completed: boolean;
}

export interface AgendaEvent extends SyncEntityBase {
  entityType: "AgendaEvent";
  title: string;
  start: string;
  end: string;
  isAllDay: boolean;
  source: AgendaEventSource;
  externalId: string | null;
  externalEtag: string | null;
  readOnly: boolean;
}

export interface Meal extends SyncEntityBase {
  entityType: "Meal";
  date: string;
  slot: MealSlot;
  recipeId: string | null;
  description: string | null;
}

export interface Recipe extends SyncEntityBase {
  entityType: "Recipe";
  title: string;
  ingredients: string[];
  steps: string[];
}

export interface Routine extends SyncEntityBase {
  entityType: "Routine";
  name: string;
  type: RoutineType;
  items: string[];
}

export interface RoutineCheck extends SyncEntityBase {
  entityType: "RoutineCheck";
  routineId: string;
  date: string;
  itemIndex: number;
  checked: boolean;
}

export interface ShoppingItem extends SyncEntityBase {
  entityType: "ShoppingItem";
  name: string;
  category: string | null;
  checked: boolean;
}

export interface InboxPage extends SyncEntityBase {
  entityType: "InboxPage";
  state: InboxPageState;
  s3Key: string | null;
  notes: string | null;
}

export type Entity =
  | Todo
  | AgendaEvent
  | Meal
  | Recipe
  | Routine
  | RoutineCheck
  | ShoppingItem
  | InboxPage;

export interface SyncRequest {
  deviceId: string;
  cursor: string | null;
  outbox: Entity[];
}

export interface SyncResponse {
  cursor: string;
  delta: Entity[];
}
