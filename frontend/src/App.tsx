// TEMPORARY — for local preview only.
// Once a teammate's real Dashboard.tsx exists, REVERT this file to
// whatever App.tsx should actually be, and instead wire
// RescueCoordinationSection into the real Dashboard tabs.

import RescueCoordinationSection from "./pages/Dashboard/sections/RescueCoordinationSection";
import "./preview-theme.css"; // TEMP — delete once real index.css/design system exists

function App() {
  return (
    <div style={{ maxWidth: 1100, margin: "0 auto" }}>
      <h1 style={{ fontSize: "1.3rem", marginBottom: 16 }}>
        Preview: Rescue Coordination (Component D)
      </h1>
      <RescueCoordinationSection />
    </div>
  );
}

export default App;
