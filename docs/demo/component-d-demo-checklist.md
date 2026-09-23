# Component D demo checklist

## Pre-demo

- [ ] Backend is running.
- [ ] Frontend is running.
- [ ] An `EmergencyCoordinator` account is available.
- [ ] Demo rescue team and vehicle are available.
- [ ] A demo Incident ID is available.
- [ ] Gemini backend configuration is confirmed; no frontend key is used.

## Demo

1. Open **Rescue coordination**.
2. Show live team and vehicle availability.
3. Create an assignment and explain its PlanVersion.
4. Run AI Safety Validation and show each PASS/FAIL check.
5. Emphasize: **AI recommendation only — human approval required.**
6. Approve and dispatch only after an APPROVE recommendation.
7. Show the team as OnMission and vehicle as InUse.
8. Progress the dispatch: En Route, On Scene, Resolved.
9. Refresh and show released team/vehicle availability.

## Safety fallback

- Demonstrate a stale-plan or insufficient-capacity rejection if a safe isolated demo record is available.
- Never use legacy dispatch create/approve routes.
- Never expose Gemini, JWT, or database credentials.
