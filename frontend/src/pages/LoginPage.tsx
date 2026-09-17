import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import "./LoginPage.css";

export default function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
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
      <form className="login-card" onSubmit={handleSubmit}>
        <h1>RescueSriLanka Admin</h1>
        <p className="login-subtitle">Coordinator &amp; Resource Manager sign in</p>

        {loginError && <div className="login-error">{loginError}</div>}

        <label>
          Email
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoFocus
          />
        </label>

        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </label>

        <button type="submit" disabled={loginLoading}>
          {loginLoading ? "Signing in…" : "Sign in"}
        </button>

        <p className="login-note">
          Admin accounts are pre-registered — there is no sign-up here.
        </p>
      </form>
    </div>
  );
}