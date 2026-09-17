import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { AuthProvider } from "./context/AuthContext";
import ProtectedRoute from "./components/ProtectedRoute";
import DashboardLayout from "./components/DashboardLayout";
import LoginPage from "./pages/LoginPage";
import HelpRequestsReview from "./pages/HelpRequestsReview";
import UserManagement from "./pages/UserManagement";
import Reports from "./pages/Reports";

function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />

          <Route element={<ProtectedRoute />}>
            <Route element={<DashboardLayout />}>
              <Route path="/dashboard/help-requests" element={<HelpRequestsReview />} />
              <Route path="/dashboard/users" element={<UserManagement />} />
              <Route path="/dashboard/reports" element={<Reports />} />
            </Route>
          </Route>

          <Route path="*" element={<Navigate to="/dashboard/help-requests" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}

export default App;