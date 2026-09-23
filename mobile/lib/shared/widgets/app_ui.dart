import 'package:flutter/material.dart';

import '../core/theme.dart';

/// The building blocks every section shares, so Help, Resources and Rescue
/// read like the disaster map and the report form rather than three separate
/// apps. Colours come from [AppColors]; nothing here hard-codes a hex value.
///
/// COLOUR RULE, same as the palette: amber is the brand accent, and
/// green / amber / orange / red mean severity or status — never decoration.
class AppSpacing {
  const AppSpacing._();

  /// Screen gutter, matching the report form.
  static const double gutter = 20;

  /// Inside a card.
  static const double card = 14;

  /// Between stacked cards.
  static const double gap = 12;

  static const BorderRadius radius = BorderRadius.all(Radius.circular(12));
}

/// A bordered white card — the one container shape used everywhere.
class AppCard extends StatelessWidget {
  const AppCard({
    super.key,
    required this.child,
    this.onTap,
    this.accent,
    this.padding,
  });

  final Widget child;
  final VoidCallback? onTap;

  /// Optional status stripe down the leading edge (severity, stock level,
  /// dispatch state).
  final Color? accent;

  final EdgeInsetsGeometry? padding;

  @override
  Widget build(BuildContext context) {
    final content = Padding(
      padding: padding ?? const EdgeInsets.all(AppSpacing.card),
      child: child,
    );

    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        border: Border.all(color: AppColors.border),
        borderRadius: AppSpacing.radius,
      ),
      clipBehavior: Clip.antiAlias,
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: onTap,
          child: accent == null
              ? content
              // The stripe is positioned rather than a Row child: a stretched
              // Row would take its height from the parent, which is unbounded
              // inside a list.
              : Stack(
                  children: [
                    Padding(
                      padding: const EdgeInsets.only(left: 4),
                      child: content,
                    ),
                    Positioned(
                      left: 0,
                      top: 0,
                      bottom: 0,
                      child: Container(width: 4, color: accent),
                    ),
                  ],
                ),
        ),
      ),
    );
  }
}

/// A small caps label above a group of cards.
class AppSectionTitle extends StatelessWidget {
  const AppSectionTitle(this.text, {super.key, this.trailing});

  final String text;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        children: [
          Expanded(
            child: Text(
              text.toUpperCase(),
              style: const TextStyle(
                fontSize: 11.5,
                fontWeight: FontWeight.w700,
                letterSpacing: 0.5,
                color: AppColors.body,
              ),
            ),
          ),
          ?trailing,
        ],
      ),
    );
  }
}

/// Status pill. [tone] carries the meaning; text always states it too, so
/// colour is never the only signal.
class AppPill extends StatelessWidget {
  const AppPill(this.label, {super.key, this.tone});

  final String label;
  final Color? tone;

  @override
  Widget build(BuildContext context) {
    final colour = tone ?? AppColors.body;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
      decoration: BoxDecoration(
        color: colour.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: TextStyle(
          fontSize: 11.5,
          fontWeight: FontWeight.w700,
          color: colour,
        ),
      ),
    );
  }
}

/// What a list shows when it has nothing to show.
class AppEmptyState extends StatelessWidget {
  const AppEmptyState({
    super.key,
    required this.icon,
    required this.title,
    this.message,
    this.action,
  });

  final IconData icon;
  final String title;
  final String? message;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 44, color: AppColors.border),
            const SizedBox(height: 14),
            Text(
              title,
              textAlign: TextAlign.center,
              style: const TextStyle(
                fontSize: 15.5,
                fontWeight: FontWeight.w600,
                color: AppColors.ink,
              ),
            ),
            if (message != null) ...[
              const SizedBox(height: 6),
              Text(
                message!,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  fontSize: 13,
                  height: 1.45,
                  color: AppColors.body,
                ),
              ),
            ],
            if (action != null) ...[const SizedBox(height: 20), action!],
          ],
        ),
      ),
    );
  }
}

/// A failure that does not hide the screen: one line, with a retry.
class AppErrorBanner extends StatelessWidget {
  const AppErrorBanner({super.key, required this.message, this.onRetry});

  final String message;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: AppColors.critical.withValues(alpha: 0.08),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 8, 8),
        child: Row(
          children: [
            const Icon(
              Icons.error_outline,
              size: 18,
              color: AppColors.critical,
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                message,
                style: const TextStyle(fontSize: 12.5, height: 1.35),
              ),
            ),
            if (onRetry != null)
              TextButton(onPressed: onRetry, child: const Text('Retry')),
          ],
        ),
      ),
    );
  }
}

/// The primary action button, in the brand amber.
class AppPrimaryButton extends StatelessWidget {
  const AppPrimaryButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.busy = false,
    this.icon,
    this.fullWidth = true,
  });

  final String label;
  final VoidCallback? onPressed;
  final bool busy;
  final IconData? icon;
  final bool fullWidth;

  @override
  Widget build(BuildContext context) {
    // While busy the spinner takes the icon's place and the label stays, so
    // the button can say what it is doing ("Uploading photo…").
    final leading = busy
        ? const SizedBox(
            width: 17,
            height: 17,
            child: CircularProgressIndicator(
              strokeWidth: 2.2,
              valueColor: AlwaysStoppedAnimation(AppColors.brandInk),
            ),
          )
        : (icon == null ? const SizedBox.shrink() : Icon(icon, size: 18));

    return FilledButton.icon(
      onPressed: busy ? null : onPressed,
      icon: leading,
      label: Text(label),
      style: FilledButton.styleFrom(
        backgroundColor: AppColors.brand,
        foregroundColor: AppColors.brandInk,
        disabledBackgroundColor: AppColors.brand.withValues(alpha: 0.5),
        minimumSize: Size(fullWidth ? double.infinity : 0, 48),
        textStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
      ),
    );
  }
}

/// A labelled figure, for the counts at the top of a section.
class AppStat extends StatelessWidget {
  const AppStat({
    super.key,
    required this.value,
    required this.label,
    this.tone,
  });

  final String value;
  final String label;
  final Color? tone;

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            value,
            style: TextStyle(
              fontSize: 19,
              fontWeight: FontWeight.w700,
              color: tone ?? AppColors.ink,
            ),
          ),
          Text(
            label,
            style: const TextStyle(fontSize: 11.5, color: AppColors.body),
          ),
        ],
      ),
    );
  }
}

/// The row of counts itself, on the tinted strip the map already uses.
class AppStatBar extends StatelessWidget {
  const AppStatBar({super.key, required this.stats});

  final List<AppStat> stats;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      decoration: const BoxDecoration(
        color: AppColors.surfaceAlt,
        border: Border(bottom: BorderSide(color: AppColors.border)),
      ),
      child: Row(children: stats),
    );
  }
}
