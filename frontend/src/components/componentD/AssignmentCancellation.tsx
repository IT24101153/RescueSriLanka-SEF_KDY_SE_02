import { useEffect, useId, useRef } from 'react'
import type { RefObject } from 'react'
import { createPortal } from 'react-dom'
import './AssignmentCancellation.css'

export default function AssignmentCancellation({ busy, error, onKeep, onConfirm, returnFocus }: {
  busy: boolean; error: string | null; onKeep: () => void; onConfirm: () => void;
  returnFocus?: RefObject<HTMLElement | null>;
}) {
  const titleId = useId()
  const messageId = useId()
  const overlay = useRef<HTMLDivElement>(null)
  const dialog = useRef<HTMLElement>(null)
  const keep = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    const fallback = returnFocus?.current
    const background = [...document.body.children].filter((element) => element !== overlay.current)
      .map((element) => ({ element, inert: element.getAttribute('inert') }))
    background.forEach(({ element }) => element.setAttribute('inert', ''))
    const { overflow, paddingRight } = document.body.style
    const scrollbar = document.documentElement.clientWidth > 0 ? window.innerWidth - document.documentElement.clientWidth : 0
    if (scrollbar > 0) document.body.style.paddingRight = `${parseFloat(getComputedStyle(document.body).paddingRight) + scrollbar}px`
    document.body.style.overflow = 'hidden'
    keep.current?.focus()
    const containFocus = (event: FocusEvent) => {
      if (!dialog.current?.contains(event.target as Node)) (keep.current?.disabled ? dialog.current : keep.current)?.focus()
    }
    document.addEventListener('focusin', containFocus)
    return () => {
      document.removeEventListener('focusin', containFocus)
      background.forEach(({ element, inert }) => { if (inert === null) element.removeAttribute('inert'); else element.setAttribute('inert', inert) })
      document.body.style.overflow = overflow
      document.body.style.paddingRight = paddingRight
      if (previous?.isConnected) previous.focus()
      else fallback?.focus()
    }
  }, [returnFocus])
  useEffect(() => { if (busy) dialog.current?.focus() }, [busy])
  return createPortal(<div ref={overlay} className="assignment-modal-overlay" onClick={(event) => {
    if (event.target === event.currentTarget && !busy) onKeep()
  }}>
    <section ref={dialog} className="assignment-modal" role="dialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={messageId} aria-busy={busy} tabIndex={-1} onKeyDown={(event) => {
      if (event.key === 'Escape') { event.preventDefault(); if (!busy) onKeep() }
      if (event.key === 'Tab') {
        const buttons = [...event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')]
        const first = buttons[0], last = buttons.at(-1)
        if (!first) { event.preventDefault(); dialog.current?.focus() }
        else if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog.current)) { event.preventDefault(); last?.focus() }
        else if (!event.shiftKey && (document.activeElement === last || document.activeElement === dialog.current)) { event.preventDefault(); first.focus() }
      }
    }}>
      <h2 id={titleId}>Cancel this assignment?</h2>
      <p id={messageId}>This response plan will be cancelled and kept in history. It will no longer reserve its rescue team or vehicle.</p>
      {error && <p className="assignment-modal__error" role="alert">{error}</p>}
      <div className="assignment-modal__actions">
        <button ref={keep} type="button" className="btn-ghost" disabled={busy} onClick={onKeep}>Keep assignment</button>
        <button type="button" className="assignment-modal__cancel" disabled={busy} onClick={onConfirm}>{busy ? 'Cancelling assignment…' : 'Cancel assignment'}</button>
      </div>
    </section>
  </div>, document.body)
}
