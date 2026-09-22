import { NavLink, Outlet } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import "./DashboardLayout.css";

const NAV_ITEMS = [
  { to: "/dashboard", label: "Dashboard", end: true, icon: <DashboardIcon /> },
  { to: "/dashboard/help-requests", label: "Help Requests", end: false, icon: <RequestIcon /> },
  { to: "/dashboard/users", label: "User Management", end: false, icon: <UsersIcon /> },
  { to: "/dashboard/reports", label: "Reports", end: false, icon: <ReportsIcon /> },
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
              end={item.end}
              className={({ isActive }) => `dash-nav-link${isActive ? " dash-nav-link--active" : ""}`}
            >
              <span className="dash-nav-icon">{item.icon}</span>
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="dash-user">
          <div className="dash-user-avatar">{(user?.name ?? "A").charAt(0)}</div>
          <div className="dash-user-info">
            <div className="dash-user-name">{user?.name ?? "Admin"}</div>
            <div className="dash-user-role">{user?.role ?? ""}</div>
          </div>
          <button className="dash-logout" onClick={logout} title="Log out">
            <LogoutIcon />
          </button>
        </div>
      </aside>
      <main className="dash-main">
        <Outlet />
      </main>
    </div>
  );
}

function RequestIcon() {
  return (
    <svg viewBox="0 0 20 20" width="17" height="17" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path d="M4 5.5A1.5 1.5 0 0 1 5.5 4h9A1.5 1.5 0 0 1 16 5.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 4 14.5v-9Z" stroke="currentColor" strokeWidth="1.4" />
      <path d="M7 8h6M7 11h4" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  );
}

function UsersIcon() {
  return (
    <svg viewBox="0 0 20 20" width="17" height="17" fill="none" xmlns="http://www.w3.org/2000/svg">
      <circle cx="7.5" cy="7" r="2.2" stroke="currentColor" strokeWidth="1.4" />
      <path d="M3.5 15.5c0-2.2 1.8-3.8 4-3.8s4 1.6 4 3.8" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
      <circle cx="13.8" cy="7.5" r="1.7" stroke="currentColor" strokeWidth="1.3" />
      <path d="M12 12.2c1.6.2 2.8 1.5 2.8 3.3" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" />
    </svg>
  );
}

function ReportsIcon() {
  return (
    <svg viewBox="0 0 20 20" width="17" height="17" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path d="M5 3.5h7l3 3V16a.5.5 0 0 1-.5.5h-9.5A.5.5 0 0 1 4.5 16V4a.5.5 0 0 1 .5-.5Z" stroke="currentColor" strokeWidth="1.4" />
      <path d="M7 10h6M7 13h6" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  );
}

function LogoutIcon() {
  return (
    <svg viewBox="0 0 20 20" width="15" height="15" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path d="M8 3.5H5a1 1 0 0 0-1 1v11a1 1 0 0 0 1 1h3" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
      <path d="M12.5 13.5 16 10l-3.5-3.5M16 10H7.5" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function DashboardIcon() {
  return (
    <svg viewBox="0 0 20 20" width="17" height="17" fill="none" xmlns="http://www.w3.org/2000/svg">
      <rect x="3.5" y="3.5" width="5" height="5" rx="1" stroke="currentColor" strokeWidth="1.4" />
      <rect x="11.5" y="3.5" width="5" height="5" rx="1" stroke="currentColor" strokeWidth="1.4" />
      <rect x="3.5" y="11.5" width="5" height="5" rx="1" stroke="currentColor" strokeWidth="1.4" />
      <rect x="11.5" y="11.5" width="5" height="5" rx="1" stroke="currentColor" strokeWidth="1.4" />
    </svg>
  );
}
