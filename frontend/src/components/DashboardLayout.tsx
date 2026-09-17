import { NavLink, Outlet } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import "./DashboardLayout.css";

const NAV_ITEMS = [
  { to: "/dashboard/help-requests", label: "Help Requests" },
  { to: "/dashboard/users", label: "User Management" },
  { to: "/dashboard/reports", label: "Reports" },
];

export default function DashboardLayout() {
  const { user, logout } = useAuth();

  return (
    <div className="dash-shell">
      <aside className="dash-sidebar">
        <div className="dash-brand">RescueSriLanka</div>
        <nav>
          {NAV_ITEMS.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) => `dash-nav-link${isActive ? " dash-nav-link--active" : ""}`}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="dash-user">
          <div className="dash-user-name">{user?.name ?? "Admin"}</div>
          <div className="dash-user-role">{user?.role ?? ""}</div>
          <button className="dash-logout" onClick={logout}>
            Log out
          </button>
        </div>
      </aside>
      <main className="dash-main">
        <Outlet />
      </main>
    </div>
  );
}