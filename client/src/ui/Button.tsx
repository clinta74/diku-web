import type { ButtonHTMLAttributes, ReactNode } from 'react'

/**
 * What this button is for, which decides how it looks.
 *
 * `quiet` is the default and needs no class — the bare `button` element rule already styles it.
 */
export type ButtonVariant = 'quiet' | 'primary' | 'danger' | 'link'

interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'className'> {
  variant?: ButtonVariant
  children: ReactNode
}

const VARIANT_CLASS: Record<ButtonVariant, string> = {
  quiet: '',
  primary: 'primary',
  danger: 'danger-button',
  link: 'link',
}

/**
 * A button whose look is chosen from a union rather than typed as a string.
 *
 * <b>This exists because a class name is not checked and a variant should be.</b> Four Remove
 * buttons in the mob editors asked for `className="danger"`, which matches no rule that applies to
 * a button — so four destructive controls rendered identically to the Add button beside them, and
 * nothing said so (UX.md finding 1). The product also carried two spellings for the one variant,
 * `danger` and `danger-button`, which is how the dead one survived.
 *
 * <b>Only variants go through here, deliberately.</b> A plain neutral button is left as a bare
 * `<button>`: the element rule already styles it correctly, it has no variant to get wrong, and
 * routing a hundred of them through a wrapper would be churn with a regression surface and no
 * defect behind it. What this component removes is the class of mistake where the *intent* was
 * "this is destructive" and the output was "this is ordinary".
 *
 * `type="button"` by default, since a button inside a form defaults to submit and none of these
 * forms want that.
 */
export function Button({ variant = 'quiet', type = 'button', children, ...rest }: ButtonProps) {
  const className = VARIANT_CLASS[variant]

  return (
    <button type={type} {...(className ? { className } : {})} {...rest}>
      {children}
    </button>
  )
}
