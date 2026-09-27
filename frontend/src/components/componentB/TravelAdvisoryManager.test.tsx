import { beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import TravelAdvisoryManager from "./TravelAdvisoryManager";
import { authFetch } from "./api";

vi.mock("./api", () => ({ authFetch: vi.fn() }));
const api = vi.mocked(authFetch);

const advisory = {
  id: "a-1", areaName: "Kandy river bank", latitude: 7.29, longitude: 80.63,
  radiusMeters: 700, safetyLevel: 2, reason: "Flood water on road",
  createdAt: "2026-09-25T08:00:00Z", updatedAt: "2026-09-25T08:00:00Z", expiresAt: null,
};

describe("Component B travel advisory management", () => {
  beforeEach(() => { cleanup(); api.mockReset(); });

  it("loads advisories through the authenticated API and filters the list", async () => {
    api.mockResolvedValue(new Response(JSON.stringify([advisory]), { status: 200 }));
    render(<TravelAdvisoryManager onBack={() => undefined} />);

    expect(await screen.findByText("Kandy river bank")).toBeTruthy();
    expect(api).toHaveBeenCalledWith("/api/TravelAdvisories/all");
    fireEvent.change(screen.getByLabelText("Search advisories"), { target: { value: "Galle" } });
    expect(await screen.findByText("No advisories match these filters.")).toBeTruthy();
  });

  it("validates and posts a new advisory, then shows success feedback", async () => {
    api.mockImplementation(async (_path, options) => {
      if (options?.method === "POST") return new Response(JSON.stringify(advisory), { status: 201 });
      return new Response(JSON.stringify([]), { status: 200 });
    });
    render(<TravelAdvisoryManager onBack={() => undefined} />);

    fireEvent.change(screen.getByLabelText("Area name"), { target: { value: "Kandy river bank" } });
    fireEvent.change(screen.getByLabelText("Latitude"), { target: { value: "7.29" } });
    fireEvent.change(screen.getByLabelText("Longitude"), { target: { value: "80.63" } });
    fireEvent.change(screen.getByLabelText("Radius (metres)"), { target: { value: "700" } });
    fireEvent.change(screen.getByLabelText("Reason"), { target: { value: "Flood water on road" } });
    fireEvent.click(screen.getByRole("button", { name: "Create advisory" }));

    expect((await screen.findByRole("status")).textContent).toContain("Advisory created.");
    const postCall = api.mock.calls.find(([, options]) => options?.method === "POST");
    expect(postCall?.[0]).toBe("/api/TravelAdvisories");
    expect(JSON.parse(String(postCall?.[1]?.body))).toMatchObject({ areaName: "Kandy river bank", safetyLevel: 1, radiusMeters: 700 });
    await waitFor(() => expect(api).toHaveBeenCalledTimes(3));
  });
});
