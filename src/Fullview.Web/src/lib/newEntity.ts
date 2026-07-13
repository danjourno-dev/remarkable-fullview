import { ulid } from "ulid";
import { getDeviceId } from "./store";
import {
  MealSlot,
  SyncContext,
  TodoEnergy,
  TodoPriority,
  type Meal,
  type Recipe,
  type ShoppingItem,
  type Todo,
} from "./types";

function base() {
  return {
    id: ulid(),
    updatedAt: new Date().toISOString(),
    updatedBy: getDeviceId(),
    deleted: false,
  };
}

export function newTodo(
  context: SyncContext,
  title: string,
  overrides: Partial<Pick<Todo, "priority" | "dueDate" | "energy">> = {},
): Todo {
  return {
    entityType: "Todo",
    ...base(),
    context,
    title,
    priority: overrides.priority ?? TodoPriority.Normal,
    dueDate: overrides.dueDate ?? null,
    energy: overrides.energy ?? (null as TodoEnergy | null),
    completed: false,
  };
}

export function newShoppingItem(
  context: SyncContext,
  name: string,
  category: string | null = null,
): ShoppingItem {
  return {
    entityType: "ShoppingItem",
    ...base(),
    context,
    name,
    category,
    checked: false,
  };
}

export function newMeal(
  date: string,
  slot: MealSlot,
  overrides: Partial<Pick<Meal, "recipeId" | "description">> = {},
): Meal {
  return {
    entityType: "Meal",
    ...base(),
    context: SyncContext.Personal,
    date,
    slot,
    recipeId: overrides.recipeId ?? null,
    description: overrides.description ?? null,
  };
}

export function newRecipe(title: string): Recipe {
  return {
    entityType: "Recipe",
    ...base(),
    context: SyncContext.Personal,
    title,
    ingredients: [],
    steps: [],
  };
}
