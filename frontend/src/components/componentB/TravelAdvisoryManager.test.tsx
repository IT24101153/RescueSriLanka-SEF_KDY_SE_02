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

function stubPlaceSearch() {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ features: [{
    geometry: { coordinates: [80.63, 7.29] },
    properties: { name: "Kandy", state: "Central Province", country: "Sri Lanka", countrycode: "LK" },
  }] }), { status: 200 }));
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

describe("Component B travel advisory management", () => {
  beforeEach(() => { cleanup(); api.mockReset(); vi.unstubAllGlobals(); });

  it("loads advisories through the authenticated API and filters the list", async () => {
    api.mockResolvedValue(new Response(JSON.stringify([advisory]), { status: 200 }));
    render(<TravelAdvisoryManager onBack={() => undefined} />);

    expect(await screen.findByText("Kandy river bank")).toBeTruthy();
    expect(api).toHaveBeenCalledWith("/api/TravelAdvisories/all");
    fireEvent.change(screen.getByLabelText("Search advisories"), { target: { value: "Galle" } });
    expect(await screen.findByText("No advisories match these filters.")).toBeTruthy();
  });

  it("sets the advisory location from a place search, posts it, then shows success feedback", async () => {
    const placeFetch = stubPlaceSearch();
    api.mockImplementation(async (_path, options) => {
      if (options?.method === "POST") return new Response(JSON.stringify(advisory), { status: 201 });
      return new Response(JSON.stringify([]), { status: 200 });
    });
    render(<TravelAdvisoryManager onBack={() => undefined} />);

    fireEvent.change(screen.getByLabelText("Area name"), { target: { value: "Kandy river bank" } });
    expect(screen.queryByLabelText("Latitude")).toBeNull();
    expect(screen.queryByLabelText("Longitude")).toBeNull();
    fireEvent.change(screen.getByLabelText("Location"), { target: { value: "Kandy" } });
    fireEvent.click(await screen.findByRole("button", { name: "Kandy, Central Province, Sri Lanka" }));
    expect(new URL(placeFetch.mock.calls[0][0]).hostname).toBe("photon.komoot.io");
    expect(screen.getByText("Kandy, Central Province, Sri Lanka")).toBeTruthy();
    expect(screen.queryByRole("list", { name: "Location search results" })).toBeNull();
    fireEvent.change(screen.getByLabelText("Radius (metres)"), { target: { value: "700" } });
    fireEvent.change(screen.getByLabelText("Reason"), { target: { value: "Flood water on road" } });
    fireEvent.click(screen.getByRole("button", { name: "Create advisory" }));

    expect((await screen.findByRole("status")).textContent).toContain("Advisory created.");
    const postCall = api.mock.calls.find(([, options]) => options?.method === "POST");
    expect(postCall?.[0]).toBe("/api/TravelAdvisories");
    expect(JSON.parse(String(postCall?.[1]?.body))).toMatchObject({ areaName: "Kandy river bank", latitude: 7.29, longitude: 80.63, safetyLevel: 1, radiusMeters: 700 });
    await waitFor(() => expect(api).toHaveBeenCalledTimes(3));
  });

  it("does not save an advisory until a location has been picked", async () => {
    api.mockResolvedValue(new Response(JSON.stringify([]), { status: 200 }));
    render(<TravelAdvisoryManager onBack={() => undefined} />);

    fireEvent.change(screen.getByLabelText("Area name"), { target: { value: "Kandy river bank" } });
    fireEvent.change(screen.getByLabelText("Reason"), { target: { value: "Flood water on road" } });
    fireEvent.click(screen.getByRole("button", { name: "Create advisory" }));

    expect((await screen.findByRole("alert")).textContent).toContain("Search for the advisory location");
    expect(api.mock.calls.some(([, options]) => options?.method === "POST")).toBe(false);
  });

  it("keeps the saved coordinates when an advisory is edited without a new search", async () => {
    api.mockImplementation(async (_path, options) => options?.method === "PUT"
      ? new Response(JSON.stringify(advisory), { status: 200 })
      : new Response(JSON.stringify([advisory]), { status: 200 }));
    render(<TravelAdvisoryManager onBack={() => undefined} />);

    fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
    expect(screen.getByText("Saved location")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

    expect((await screen.findByRole("status")).textContent).toContain("Advisory updated.");
    const putCall = api.mock.calls.find(([, options]) => options?.method === "PUT");
    expect(putCall?.[0]).toBe("/api/TravelAdvisories/a-1");
    expect(JSON.parse(String(putCall?.[1]?.body))).toMatchObject({ latitude: 7.29, longitude: 80.63 });
  });
});
