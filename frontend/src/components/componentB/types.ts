// API shapes for the help-request screens. Enum fields arrive as numbers
// (the backend's HelpRequestDtos pin them with JsonNumberEnumConverter).

export interface HelpRequestDto {
  id: string;
  citizenId: string;
  type: number;
  description: string;
  latitude: number;
  longitude: number;
  urgencyScore: number;
  status: number;
  verificationStatus: number;
  verificationNotes: string | null;
  imageUrl: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface StatusHistoryDto {
  oldStatus: number;
  newStatus: number;
  notes: string | null;
  changedAt: string;
}
