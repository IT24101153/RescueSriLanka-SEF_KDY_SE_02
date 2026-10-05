import { beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import HelpRequestGuidance from "./HelpRequestGuidance";
import { authFetch } from "./api";

vi.mock("./api", () => ({ authFetch: vi.fn() }));
const api = vi.mocked(authFetch);

const sent = {
  id: "m-1", message: "A team has been informed.", doItems: ["Stay on the upper floor"],
  dontItems: ["Do not go outside"], isCritical: true, createdAt: "2026-10-05T10:00:00Z",
};

function respond(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status });
}

describe("Help request guidance for the citizen", () => {
  beforeEach(() => { cleanup(); api.mockReset(); });

  it("lists what has already been sent, with its Do and Don't items", async () => {
    api.mockResolvedValue(respond([sent]));
    render(<HelpRequestGuidance requestId="r-1" />);

    expect(await screen.findByText("A team has been informed.")).toBeTruthy();
    expect(screen.getByText("Stay on the upper floor")).toBeTruthy();
    expect(screen.getByText("Do not go outside")).toBeTruthy();
    expect(screen.getByText(/^Urgent ·/)).toBeTruthy();
    expect(api).toHaveBeenCalledWith("/api/HelpRequests/r-1/messages");
  });

  it("sends the note and one item per line, trimmed, then reloads the list", async () => {
    let body: Record<string, unknown> | undefined;
    api.mockImplementation(async (_path: string, options?: RequestInit) => {
      if (options?.method === "POST") {
        body = JSON.parse(options.body as string);
        return respond({}, 201);
      }
      return respond([]);
    });
    render(<HelpRequestGuidance requestId="r-1" />);
    await screen.findByText("Nothing sent yet.");

    fireEvent.change(screen.getByPlaceholderText(/A team has been informed/), { target: { value: " Stay calm " } });
    fireEvent.change(screen.getByLabelText(/Do \(one per line\)/), { target: { value: "Move upstairs\n\n  Keep your phone on  " } });
    fireEvent.change(screen.getByLabelText(/Don't \(one per line\)/), { target: { value: "Do not go outside" } });
    fireEvent.click(screen.getByLabelText(/Urgent/));
    fireEvent.click(screen.getByRole("button", { name: "Send to citizen" }));

    await waitFor(() => expect(body).toBeDefined());
    expect(body).toEqual({
      message: "Stay calm",
      doItems: ["Move upstairs", "Keep your phone on"],
      dontItems: ["Do not go outside"],
      isCritical: true,
    });
    expect(api).toHaveBeenCalledWith("/api/HelpRequests/r-1/messages", expect.objectContaining({ method: "POST" }));
  });

  it("will not send an empty message", async () => {
    api.mockResolvedValue(respond([]));
    render(<HelpRequestGuidance requestId="r-1" />);
    await screen.findByText("Nothing sent yet.");

    fireEvent.click(screen.getByRole("button", { name: "Send to citizen" }));

    expect((await screen.findByRole("alert")).textContent).toMatch(/Write a note/);
    expect(api).not.toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ method: "POST" }));
  });

  it("shows the reason the API gives when it refuses a message", async () => {
    api.mockImplementation(async (_path: string, options?: RequestInit) =>
      options?.method === "POST" ? respond({ message: "Each item must be 200 characters or fewer." }, 400) : respond([]));
    render(<HelpRequestGuidance requestId="r-1" />);
    await screen.findByText("Nothing sent yet.");

    fireEvent.change(screen.getByLabelText(/Do \(one per line\)/), { target: { value: "x" } });
    fireEvent.click(screen.getByRole("button", { name: "Send to citizen" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Each item must be 200 characters or fewer.");
  });

  it("fills the lists from a template", async () => {
    api.mockResolvedValue(respond([]));
    render(<HelpRequestGuidance requestId="r-1" />);
    await screen.findByText("Nothing sent yet.");

    fireEvent.change(screen.getByLabelText("Insert a template"), { target: { value: "Flood" } });

    expect((screen.getByLabelText(/Do \(one per line\)/) as HTMLTextAreaElement).value).toContain("Move to the highest floor");
    expect((screen.getByLabelText(/Don't \(one per line\)/) as HTMLTextAreaElement).value).toContain("Do not walk or drive through flood water");
  });

  it("takes a message back", async () => {
    let deleted = false;
    api.mockImplementation(async (_path: string, options?: RequestInit) => {
      if (options?.method === "DELETE") { deleted = true; return new Response(null, { status: 204 }); }
      return respond(deleted ? [] : [sent]);
    });
    render(<HelpRequestGuidance requestId="r-1" />);
    await screen.findByText("A team has been informed.");

    fireEvent.click(screen.getByRole("button", { name: "Take back" }));

    expect(await screen.findByText("Nothing sent yet.")).toBeTruthy();
    expect(api).toHaveBeenCalledWith("/api/HelpRequests/r-1/messages/m-1", { method: "DELETE" });
  });
});
