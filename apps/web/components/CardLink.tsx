"use client";
import Link from "next/link";
import { motion, useReducedMotion } from "framer-motion";
import type { ReactNode } from "react";

const MotionLink = motion.create(Link);

export function CardLink({
  href,
  className,
  children,
}: {
  href: string;
  className: string;
  children: ReactNode;
}) {
  const reduced = useReducedMotion();
  return (
    <MotionLink
      href={href}
      className={className}
      whileHover={reduced ? undefined : { y: -2 }}
      transition={{ duration: 0.2 }}
    >
      {children}
    </MotionLink>
  );
}
