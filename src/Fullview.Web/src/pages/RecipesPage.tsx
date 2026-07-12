import { useState } from "react";
import { newRecipe, newShoppingItem } from "../lib/newEntity";
import { getDeviceId, putLocal } from "../lib/store";
import { SyncContext, type Recipe } from "../lib/types";
import { useEntities } from "../lib/useStore";

function touch(recipe: Recipe, changes: Partial<Recipe>): Recipe {
  return { ...recipe, ...changes, updatedAt: new Date().toISOString(), updatedBy: getDeviceId() };
}

function linesToList(text: string): string[] {
  return text
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean);
}

export function RecipesPage() {
  const recipes = useEntities<Recipe>("Recipe").sort((a, b) => a.title.localeCompare(b.title));
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [newTitle, setNewTitle] = useState("");

  const selected = recipes.find((r) => r.id === selectedId) ?? null;

  function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    const trimmed = newTitle.trim();
    if (!trimmed) return;
    const recipe = newRecipe(trimmed);
    putLocal(recipe);
    setSelectedId(recipe.id);
    setNewTitle("");
  }

  function addIngredientsToShopping(recipe: Recipe) {
    for (const ingredient of recipe.ingredients) {
      putLocal(newShoppingItem(SyncContext.Personal, ingredient));
    }
  }

  return (
    <div className="page recipes-page">
      <div className="recipes-list">
        <form className="quick-add" onSubmit={handleCreate}>
          <input
            type="text"
            placeholder="New recipe title…"
            value={newTitle}
            onChange={(e) => setNewTitle(e.target.value)}
          />
          <button type="submit">Add</button>
        </form>
        <ul className="list">
          {recipes.map((recipe) => (
            <li key={recipe.id} className={recipe.id === selectedId ? "list-item selected" : "list-item"}>
              <button className="link-button" onClick={() => setSelectedId(recipe.id)}>
                {recipe.title}
              </button>
            </li>
          ))}
          {recipes.length === 0 && <li className="empty">No recipes yet.</li>}
        </ul>
      </div>
      {selected && (
        <div className="recipe-editor">
          <input
            type="text"
            value={selected.title}
            onChange={(e) => putLocal(touch(selected, { title: e.target.value }))}
          />
          <label>
            Ingredients (one per line)
            <textarea
              rows={8}
              value={selected.ingredients.join("\n")}
              onChange={(e) => putLocal(touch(selected, { ingredients: linesToList(e.target.value) }))}
            />
          </label>
          <label>
            Steps (one per line)
            <textarea
              rows={8}
              value={selected.steps.join("\n")}
              onChange={(e) => putLocal(touch(selected, { steps: linesToList(e.target.value) }))}
            />
          </label>
          <button onClick={() => addIngredientsToShopping(selected)}>Add ingredients to shopping</button>
          <button
            className="danger"
            onClick={() => {
              putLocal(touch(selected, { deleted: true }));
              setSelectedId(null);
            }}
          >
            Delete recipe
          </button>
        </div>
      )}
    </div>
  );
}
