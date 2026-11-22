import { Slot } from "@radix-ui/react-slot";
import { cva } from "class-variance-authority";
import type { ComponentProps } from "react";
const styles = cva("button", {
  variants: { variant: { primary: "primary", outline: "outline" } },
  defaultVariants: { variant: "primary" },
});
export function Button({
  asChild,
  variant,
  className = "",
  ...props
}: ComponentProps<"button"> & {
  asChild?: boolean;
  variant?: "primary" | "outline";
}) {
  const Component = asChild ? Slot : "button";
  return <Component className={styles({ variant, className })} {...props} />;
}
