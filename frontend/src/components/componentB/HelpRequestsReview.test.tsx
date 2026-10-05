import { beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import HelpRequestsReview from "./HelpRequestsReview";
import { authFetch } from "./api";

vi.mock("./api", () => ({ authFetch: vi.fn() }));
const api = vi.mocked(authFetch);

const request = {
  id: "r-1", citizenId: "c-1", citizenName: "Nimal Perera", citizenPhoneNumber: "0771234567",
  type: 4, description: "Trapped on roof, water rising", latitude: 7, longitude: 80,
  urgencyScore: 90, status: 0, verificationStatus: 0, verificationNotes: null, imageUrl: null,
  createdAt: "2026-09-01T00:00:00Z", updatedAt: "2026-09-01T00:00:00Z",
};

function respond(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status });
}

function mockRoutes(
  aiPriority: unknown,
  onDecision?: (approved: boolean) => void,
  requestOverride: Partial<typeof request> = {},
  onStatus?: (newStatus: number) => void,
) {
  api.mockImplementation(async (path: string, options?: RequestInit) => {
    if (path === "/api/HelpRequests") return respond([{ ...request, ...requestOverride }]);
    if (path.endsWith("/status") && options?.method === "PATCH") {
      onStatus?.((JSON.parse(options.body as string) as { newStatus: number }).newStatus);
      return respond({});
    }
    if (path.endsWith("/history")) return respond([]);
    if (path.endsWith("/ai-priority")) return respond(aiPriority);
    if (path.endsWith("/ai-analysis") && options?.method === "POST") return respond({});
    if (path.startsWith("/api/agentworkflows/") && path.endsWith("/decision") && options?.method === "POST") {
      onDecision?.((JSON.parse(options.body as string) as { approved: boolean }).approved);
      return respond({});
    }
    return respond({});
  });
}

describe("Component B help request review — AI assessment", () => {
  beforeEach(() => { cleanup(); api.mockReset(); });

  it("auto-loads the Planner Agent's assessment without any click, and shows it above the verify controls", async () => {
    mockRoutes({
      priority: "High", aiAnalysisAvailable: true,
      reasoning: "Flooding reported near a known hazard zone.",
      suggestedAction: "Dispatch a rescue team promptly.",
      credibilitySignal: "Looks genuine",
    });
    render(<HelpRequestsReview />);

    await screen.findByText("Reported by: Nimal Perera · 0771234567");
    expect(await screen.findByText("Priority: High")).toBeTruthy();
    expect(api).toHaveBeenCalledWith("/api/HelpRequests/r-1/ai-priority");
    expect(await screen.findByText("Flooding reported near a known hazard zone.")).toBeTruthy();
    expect(screen.getByText("Dispatch a rescue team promptly.")).toBeTruthy();
    expect(screen.getByText("Looks genuine")).toBeTruthy();
    expect(screen.getByText("This report needs verification")).toBeTruthy();
    expect((screen.getByRole("button", { name: "Assign" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText("Verify this request before assigning it.")).toBeTruthy();
  });

  it("shows a pending state, not an error, when no analysis has completed yet", async () => {
    mockRoutes({ priority: "Analysis pending", aiAnalysisAvailable: false });
    render(<HelpRequestsReview />);

    await screen.findByText("Reported by: Nimal Perera · 0771234567");
    expect(await screen.findByText("Priority: Analysis pending")).toBeTruthy();
    expect(screen.getByText(/Gemini reasoning isn't available/)).toBeTruthy();
  });

  it("re-running AI review re-fetches the read-only assessment instead of trusting the POST body", async () => {
    mockRoutes({ priority: "Analysis pending", aiAnalysisAvailable: false });
    render(<HelpRequestsReview />);
    await screen.findByText("Priority: Analysis pending");

    mockRoutes({
      priority: "High", aiAnalysisAvailable: true,
      reasoning: "Updated assessment.", suggestedAction: "Dispatch now.", credibilitySignal: "Looks genuine",
    });
    fireEvent.click(screen.getByRole("button", { name: "Run AI review" }));

    expect(await screen.findByText("Updated assessment.")).toBeTruthy();
    expect(api).toHaveBeenCalledWith("/api/HelpRequests/r-1/ai-analysis", { method: "POST" });
  });

  it("shows the citizen's name and phone number for the Help Request Manager to see", async () => {
    mockRoutes({ priority: "Analysis pending", aiAnalysisAvailable: false });
    render(<HelpRequestsReview />);

    expect(await screen.findByText("Reported by: Nimal Perera · 0771234567")).toBeTruthy();
  });

  it("offers Approve/Reject plan on the AI's plan only while it's awaiting approval, separate from Verify/Reject", async () => {
    mockRoutes({
      priority: "High", aiAnalysisAvailable: true,
      reasoning: "Flooding reported.", suggestedAction: "Dispatch now.", credibilitySignal: "Looks genuine",
      workflowId: "wf-1", workflowStatus: "AwaitingApproval",
    });
    render(<HelpRequestsReview />);

    expect(await screen.findByRole("button", { name: /Approve plan/ })).toBeTruthy();
    expect(screen.getByRole("button", { name: /Reject plan/ })).toBeTruthy();
    // Distinct from the credibility Verify/Reject controls, which are still present.
    expect(screen.getByRole("button", { name: "Verify" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Reject" })).toBeTruthy();
  });

  it("approving the plan posts a decision to the workflow, not the help request, and reflects the decided state", async () => {
    let approvedWith: boolean | undefined;
    mockRoutes(
      { priority: "High", aiAnalysisAvailable: true, workflowId: "wf-1", workflowStatus: "AwaitingApproval" },
      (approved) => { approvedWith = approved; },
    );
    render(<HelpRequestsReview />);
    await screen.findByRole("button", { name: /Approve plan/ });

    mockRoutes(
      { priority: "High", aiAnalysisAvailable: true, workflowId: "wf-1", workflowStatus: "Approved" },
      (approved) => { approvedWith = approved; },
    );
    fireEvent.click(screen.getByRole("button", { name: /Approve plan/ }));

    expect(await screen.findByText("Plan approved.")).toBeTruthy();
    expect(approvedWith).toBe(true);
    expect(api).toHaveBeenCalledWith("/api/agentworkflows/wf-1/decision", expect.objectContaining({ method: "POST" }));
    expect(screen.queryByRole("button", { name: /Approve plan/ })).toBeNull();
  });

  it("shows no decision controls once a plan has already been decided", async () => {
    mockRoutes({ priority: "High", aiAnalysisAvailable: true, workflowId: "wf-1", workflowStatus: "Rejected" });
    render(<HelpRequestsReview />);

    expect(await screen.findByText("Plan rejected.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Approve plan/ })).toBeNull();
    expect(screen.queryByRole("button", { name: /Reject plan/ })).toBeNull();
  });
});

describe("Component B help request review — status flow", () => {
  const pending = { aiPriority: { priority: "High", aiAnalysisAvailable: false } };

  beforeEach(() => { cleanup(); api.mockReset(); });

  it("offers the next step as a button, and moves the request when it is pressed", async () => {
    let sent: number | undefined;
    mockRoutes(pending.aiPriority, undefined, { verificationStatus: 1 }, (status) => { sent = status; });
    render(<HelpRequestsReview />);

    const assign = await screen.findByRole("button", { name: "Assign" }) as HTMLButtonElement;
    expect(assign.disabled).toBe(false);
    expect(screen.queryByText("Verify this request before assigning it.")).toBeNull();
    // Only the steps that can happen now are offered.
    expect(screen.queryByRole("button", { name: "Start work" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Mark resolved" })).toBeNull();

    fireEvent.click(assign);

    await waitFor(() => expect(sent).toBe(1));
  });

  it("shows where the request is in its progress", async () => {
    mockRoutes(pending.aiPriority, undefined, { verificationStatus: 1, status: 2 });
    render(<HelpRequestsReview />);

    const steps = await screen.findAllByRole("listitem");
    const progress = steps.filter((step) => ["Pending", "Assigned", "In Progress", "Resolved"].includes(step.textContent?.replace(/^\d+/, "") ?? ""));
    expect(progress.map((step) => step.getAttribute("aria-current"))).toEqual([null, null, "step", null]);
    expect(await screen.findByRole("button", { name: "Mark resolved" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Assign" })).toBeNull();
  });

  it("asks before cancelling, and only cancels once confirmed", async () => {
    let sent: number | undefined;
    mockRoutes(pending.aiPriority, undefined, { verificationStatus: 1 }, (status) => { sent = status; });
    render(<HelpRequestsReview />);

    fireEvent.click(await screen.findByRole("button", { name: "Cancel request" }));
    expect(await screen.findByText(/Cancel this request\?/)).toBeTruthy();
    expect(sent).toBeUndefined();

    fireEvent.click(screen.getByRole("button", { name: "Keep it" }));
    expect(screen.queryByText(/Cancel this request\?/)).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Cancel request" }));
    fireEvent.click(await screen.findByRole("button", { name: "Yes, cancel it" }));
    await waitFor(() => expect(sent).toBe(4));
  });

  it("offers no actions once a request is resolved or cancelled", async () => {
    mockRoutes(pending.aiPriority, undefined, { verificationStatus: 1, status: 3 });
    const { unmount } = render(<HelpRequestsReview />);
    expect(await screen.findByText(/This request is resolved/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Cancel request" })).toBeNull();
    unmount();

    mockRoutes(pending.aiPriority, undefined, { verificationStatus: 1, status: 4 });
    render(<HelpRequestsReview />);
    expect(await screen.findByText("This request was cancelled.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Cancel request" })).toBeNull();
  });
});

describe("Component B help request review — rescue-managed types", () => {
  beforeEach(() => { cleanup(); api.mockReset(); });

  it.each([3])("leaves status to the Rescue Coordinator for type %i", async (type) => {
    mockRoutes({ priority: "High", aiAnalysisAvailable: false }, undefined, { verificationStatus: 1, type });
    render(<HelpRequestsReview />);
    expect(await screen.findByText(/Rescue Coordinator handles this request/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Cancel request" })).toBeNull();
  });

  it("offers only Verify and Reject on an unverified rescue report, with no plan decision", async () => {
    mockRoutes({ priority: "High", aiAnalysisAvailable: false, workflowId: "w-1", workflowStatus: "AwaitingApproval" }, undefined, { type: 3 });
    render(<HelpRequestsReview />);
    expect(await screen.findByRole("button", { name: /Verify/ })).toBeTruthy();
    expect(screen.getByRole("button", { name: /Reject$/ })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Approve plan/ })).toBeNull();
    expect(screen.queryByRole("button", { name: /Reject plan/ })).toBeNull();
  });
});
