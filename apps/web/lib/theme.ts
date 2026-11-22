export const themeCookie = "fragsplit-theme";
export type Theme = "system" | "light" | "dark";

export function parseTheme(value?: string): Theme {
  return value === "dark" || value === "light" ? value : "system";
}
