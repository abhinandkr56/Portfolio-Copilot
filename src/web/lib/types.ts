// Short names for the generated schemas. Never hand-write DTO shapes: run `npm run gen:api` instead.
import type { components } from "./api-types";

type Schemas = components["schemas"];

export type AllocationClass = Schemas["AllocationClassDto"];
export type AllocationResponse = Schemas["AllocationResponse"];
export type AssetClass = Schemas["AssetClass"];
export type FailedOrder = Schemas["FailedOrderDto"];
export type Finding = Schemas["FindingDto"];
export type FindingCounts = Schemas["FindingCounts"];
export type FundRow = Schemas["FundRow"];
export type HoldingsResponse = Schemas["HoldingsResponse"];
export type HoldingsTotal = Schemas["HoldingsTotal"];
export type Plan = Schemas["Plan"];
export type PlanType = Schemas["PlanType"];
export type PortfolioReview = Schemas["PortfolioReview"];
export type SessionResponse = Schemas["SessionResponse"];
export type Severity = Schemas["Severity"];
export type SipDto = Schemas["SipDto"];
export type SipsResponse = Schemas["SipsResponse"];
export type StockRow = Schemas["StockRow"];
export type AiInfo = Schemas["AiInfo"];
export type AskRequest = Schemas["AskRequest"];
export type AskResponse = Schemas["AskResponse"];
