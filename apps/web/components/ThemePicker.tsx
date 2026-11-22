"use client";
import { Moon, Sun, SunMoon } from "lucide-react";
import { useState } from "react";
import { parseTheme, themeCookie, type Theme } from "@/lib/theme";

export function ThemePicker({ initialTheme }: { initialTheme: Theme }) {
  const [theme, setTheme] = useState(initialTheme);
  const Icon = theme === "system" ? SunMoon : theme === "dark" ? Moon : Sun;

  function changeTheme(value: string) {
    const next = parseTheme(value);
    document.documentElement.dataset.theme = next;
    setTheme(next);
    const age = next === "system" ? 0 : 365 * 24 * 60 * 60;
    const secure = location.protocol === "https:" ? "; Secure" : "";
    document.cookie = `${themeCookie}=${next}; Path=/; Max-Age=${age}; SameSite=Lax${secure}`;
  }

  return (
    <label className="icon theme-picker" title="Color theme">
      <Icon size={19} aria-hidden />
      <span className="sr-only">Color theme</span>
      <select
        value={theme}
        onChange={(event) => changeTheme(event.target.value)}
      >
        <option value="system">Use device theme</option>
        <option value="light">Light</option>
        <option value="dark">Dark</option>
      </select>
    </label>
  );
}
