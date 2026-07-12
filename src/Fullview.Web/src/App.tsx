import { Navigate, Route, Routes } from "react-router-dom";
import { AuthGate } from "./components/AuthGate";
import { Header } from "./components/Header";
import { AppProvider } from "./context/AppContext";
import { InboxPage } from "./pages/InboxPage";
import { MealsPage } from "./pages/MealsPage";
import { RecipesPage } from "./pages/RecipesPage";
import { ShoppingPage } from "./pages/ShoppingPage";
import { StatusPage } from "./pages/StatusPage";
import { TodosPage } from "./pages/TodosPage";

export function App() {
  return (
    <AuthGate>
      <AppProvider>
        <Header />
        <main>
          <Routes>
            <Route path="/" element={<Navigate to="/todos" replace />} />
            <Route path="/todos" element={<TodosPage />} />
            <Route path="/shopping" element={<ShoppingPage />} />
            <Route path="/meals" element={<MealsPage />} />
            <Route path="/recipes" element={<RecipesPage />} />
            <Route path="/inbox" element={<InboxPage />} />
            <Route path="/status" element={<StatusPage />} />
          </Routes>
        </main>
      </AppProvider>
    </AuthGate>
  );
}
