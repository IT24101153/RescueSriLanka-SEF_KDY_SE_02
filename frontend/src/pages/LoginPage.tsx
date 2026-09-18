import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import "./LoginPage.css";

export default function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [keepSignedIn, setKeepSignedIn] = useState(true);
  const { login, loginError, loginLoading } = useAuth();
  const navigate = useNavigate();

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    try {
      await login(email, password);
      navigate("/dashboard/help-requests");
    } catch {
      // error already captured in loginError via context
    }
  }

  return (
    <div className="login-page">
      <div className="login-brand-panel">
        <div className="login-brand-mark">
          <svg viewBox="0 0 24 24" width="22" height="22" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path
              d="M12 2 20 5.5V11C20 16 16.5 20 12 22 7.5 20 4 16 4 11V5.5L12 2Z"
              fill="currentColor"
            />
          </svg>
          <span>RescueSriLanka</span>
        </div>

        <h1 className="login-headline">
          Coordinate the response,
          <br />
          from one console.
        </h1>
        <p className="login-lede">
          Live incident mapping, safety zones and AI-assisted severity analysis for
          emergency response teams across Sri Lanka.
        </p>

        <ul className="login-checklist">
          <li>
            <CheckIcon /> Live disaster map with safety zones
          </li>
          <li>
            <CheckIcon /> AI severity analysis with human approval
          </li>
          <li>
            <CheckIcon /> Teams, shelters and supplies in one place
          </li>
        </ul>

        <div className="login-legend">
          <span className="login-legend-item">
            <span className="login-dot login-dot--safe" /> Safe
          </span>
          <span className="login-legend-item">
            <span className="login-dot login-dot--caution" /> Caution
          </span>
          <span className="login-legend-item">
            <span className="login-dot login-dot--danger" /> Danger
          </span>
        </div>
      </div>

      <div className="login-form-panel">
        <form className="login-form" onSubmit={handleSubmit}>
          <h2>Sign in</h2>
          <p className="login-form-subtitle">
            Emergency coordination console — authorised personnel only.
          </p>

          {loginError && <div className="login-error">{loginError}</div>}

          <label>
            Email
            <input
              type="email"
              placeholder="coordinator@rescue.lk"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              autoFocus
            />
          </label>

          <label>
            Password
            <div className="login-password-field">
              <input
                type={showPassword ? "text" : "password"}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
              <button
                type="button"
                className="login-show-toggle"
                onClick={() => setShowPassword((v) => !v)}
              >
                {showPassword ? "Hide" : "Show"}
              </button>
            </div>
          </label>

          <div className="login-row">
            <label className="login-checkbox">
              <input
                type="checkbox"
                checked={keepSignedIn}
                onChange={(e) => setKeepSignedIn(e.target.checked)}
              />
              Keep me signed in
            </label>
            {/* Placeholder — no password-reset flow built yet */}
            <button type="button" className="login-forgot">
              Forgot password?
            </button>
          </div>

          <button type="submit" className="login-submit" disabled={loginLoading}>
            {loginLoading ? "Signing in…" : "Sign in"}
          </button>

          <p className="login-footer-note">Accounts are provisioned by your administrator.</p>
        </form>
      </div>
    </div>
  );
}

function CheckIcon() {
  return (
    <svg viewBox="0 0 16 16" width="14" height="14" fill="none" xmlns="http://www.w3.org/2000/svg">
      <circle cx="8" cy="8" r="8" fill="currentColor" opacity="0.15" />
      <path d="M4.5 8.2 6.8 10.5 11.5 5.5" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}