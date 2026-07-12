import { useState } from "react";
import { newMeal } from "../lib/newEntity";
import { getDeviceId, putLocal } from "../lib/store";
import { MealSlot, type Meal, type Recipe } from "../lib/types";
import { useEntities } from "../lib/useStore";

function touch(meal: Meal, changes: Partial<Meal>): Meal {
  return { ...meal, ...changes, updatedAt: new Date().toISOString(), updatedBy: getDeviceId() };
}

function mondayOf(date: Date): Date {
  const d = new Date(date);
  const day = d.getDay();
  const diff = day === 0 ? -6 : 1 - day;
  d.setDate(d.getDate() + diff);
  d.setHours(0, 0, 0, 0);
  return d;
}

function toIsoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

const SLOTS: { slot: MealSlot; label: string }[] = [
  { slot: MealSlot.Breakfast, label: "Breakfast" },
  { slot: MealSlot.Dinner, label: "Dinner" },
];

export function MealsPage() {
  const [weekStart, setWeekStart] = useState(() => mondayOf(new Date()));
  const meals = useEntities<Meal>("Meal");
  const recipes = useEntities<Recipe>("Recipe").sort((a, b) => a.title.localeCompare(b.title));

  const days = Array.from({ length: 7 }, (_, i) => {
    const d = new Date(weekStart);
    d.setDate(d.getDate() + i);
    return d;
  });

  function mealFor(date: string, slot: MealSlot): Meal | undefined {
    return meals.find((m) => m.date === date && m.slot === slot);
  }

  function setRecipe(date: string, slot: MealSlot, recipeId: string) {
    const existing = mealFor(date, slot);
    if (existing) {
      putLocal(touch(existing, { recipeId: recipeId || null, description: null }));
    } else if (recipeId) {
      putLocal(newMeal(date, slot, { recipeId }));
    }
  }

  function setDescription(date: string, slot: MealSlot, description: string) {
    const existing = mealFor(date, slot);
    if (existing) {
      putLocal(touch(existing, { description: description || null, recipeId: null }));
    } else if (description) {
      putLocal(newMeal(date, slot, { description }));
    }
  }

  return (
    <div className="page">
      <div className="week-nav">
        <button onClick={() => setWeekStart((w) => { const d = new Date(w); d.setDate(d.getDate() - 7); return d; })}>
          ← prev
        </button>
        <span>{toIsoDate(days[0])} – {toIsoDate(days[6])}</span>
        <button onClick={() => setWeekStart((w) => { const d = new Date(w); d.setDate(d.getDate() + 7); return d; })}>
          next →
        </button>
      </div>
      <table className="meal-grid">
        <thead>
          <tr>
            <th></th>
            {days.map((d) => (
              <th key={toIsoDate(d)}>{d.toLocaleDateString(undefined, { weekday: "short", day: "numeric" })}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {SLOTS.map(({ slot, label }) => (
            <tr key={slot}>
              <th>{label}</th>
              {days.map((d) => {
                const date = toIsoDate(d);
                const meal = mealFor(date, slot);
                return (
                  <td key={date}>
                    <select value={meal?.recipeId ?? ""} onChange={(e) => setRecipe(date, slot, e.target.value)}>
                      <option value="">— recipe —</option>
                      {recipes.map((r) => (
                        <option key={r.id} value={r.id}>
                          {r.title}
                        </option>
                      ))}
                    </select>
                    <input
                      type="text"
                      placeholder="or custom…"
                      value={meal?.recipeId ? "" : meal?.description ?? ""}
                      disabled={!!meal?.recipeId}
                      onChange={(e) => setDescription(date, slot, e.target.value)}
                    />
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
