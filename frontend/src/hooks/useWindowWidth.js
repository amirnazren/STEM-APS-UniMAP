/**
 * @file Responsive helper hook — returns the current viewport width and breakpoint.
 */
import { useEffect, useState } from 'react';

/** Breakpoints: desktop >= 1280px, tablet >= 768px, otherwise mobile. */
export const BREAKPOINTS = Object.freeze({ desktop: 1280, tablet: 768 });

/**
 * Track the viewport width (rAF-throttled resize listener).
 * @returns {{ width: number, isDesktop: boolean, isTablet: boolean, isMobile: boolean }}
 */
export default function useWindowWidth() {
  const [width, setWidth] = useState(() => (typeof window === 'undefined' ? BREAKPOINTS.desktop : window.innerWidth));

  useEffect(() => {
    let frame = null;
    const onResize = () => {
      if (frame) return;
      frame = requestAnimationFrame(() => {
        frame = null;
        setWidth(window.innerWidth);
      });
    };
    window.addEventListener('resize', onResize);
    return () => {
      window.removeEventListener('resize', onResize);
      if (frame) cancelAnimationFrame(frame);
    };
  }, []);

  return {
    width,
    isDesktop: width >= BREAKPOINTS.desktop,
    isTablet: width >= BREAKPOINTS.tablet && width < BREAKPOINTS.desktop,
    isMobile: width < BREAKPOINTS.tablet
  };
}
