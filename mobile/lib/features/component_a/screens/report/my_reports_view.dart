import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../../../shared/core/theme.dart';
import '../../../../shared/widgets/app_ui.dart';
import '../../models/incident.dart';
import '../../widgets/incident_photos.dart';
import 'report_visuals.dart';

/// The reports this person has sent, each with where it is in review.
///
/// A report is only worth sending if you can see something happen to it, so
/// every card leads with its progress rather than its details.
class MyReportsView extends StatelessWidget {
  const MyReportsView({
    super.key,
    required this.reports,
    required this.loading,
    required this.error,
    required this.onRefresh,
    required this.onNewReport,
    required this.bottomInset,
  });

  final List<Incident> reports;
  final bool loading;
  final String? error;
  final Future<void> Function() onRefresh;
  final VoidCallback onNewReport;

  /// Room left under the list for the floating tab bar.
  final double bottomInset;

  @override
  Widget build(BuildContext context) {
    if (reports.isEmpty) {
      return RefreshIndicator(
        onRefresh: onRefresh,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: EdgeInsets.fromLTRB(16, 48, 16, bottomInset),
          children: [
            AppEmptyState(
              icon: Icons.inbox_outlined,
              title: 'No reports yet',
              message:
                  'Reports you send appear here, so you can follow each one '
                  'as a coordinator reviews it.',
              action: FilledButton.icon(
                onPressed: onNewReport,
                style: FilledButton.styleFrom(
                  backgroundColor: AppColors.brand,
                  foregroundColor: AppColors.brandInk,
                  minimumSize: const Size(200, 46),
                ),
                icon: const Icon(Icons.add_alert_outlined, size: 19),
                label: const Text('File a report'),
              ),
            ),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: onRefresh,
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: EdgeInsets.fromLTRB(16, 16, 16, bottomInset),
        children: [
          if (error != null) ...[
            _Notice(message: error!),
            const SizedBox(height: 12),
          ],
          _Summary(reports: reports),
          const SizedBox(height: 20),
          AppSectionTitle(
            'Your reports',
            trailing: Text(
              loading ? 'Checking for updates…' : 'Pull down to update',
              style: const TextStyle(fontSize: 11.5, color: AppColors.body),
            ),
          ),
          for (final report in reports) ...[
            _ReportCard(
              report: report,
              onTap: () => showReportDetail(context, report),
            ),
            const SizedBox(height: AppSpacing.gap),
          ],
        ],
      ),
    );
  }
}

// ------------------------------------------------------------------ summary

class _Summary extends StatelessWidget {
  const _Summary({required this.reports});

  final List<Incident> reports;

  @override
  Widget build(BuildContext context) {
    int count(bool Function(String status) test) =>
        reports.where((report) => test(report.status)).length;

    return AppCard(
      child: Row(
        children: [
          AppStat(value: '${reports.length}', label: 'Sent'),
          AppStat(
            value: '${count((s) => s == 'Reported')}',
            label: 'In review',
          ),
          AppStat(
            value: '${count(isApproved)}',
            label: 'Approved',
            tone: AppColors.low,
          ),
          AppStat(
            value: '${count((s) => s == 'Rejected')}',
            label: 'Rejected',
            tone: AppColors.critical,
          ),
        ],
      ),
    );
  }
}

class _Notice extends StatelessWidget {
  const _Notice({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppColors.high.withValues(alpha: 0.08),
        border: Border.all(color: AppColors.high.withValues(alpha: 0.3)),
        borderRadius: AppSpacing.radius,
      ),
      child: Row(
        children: [
          const Icon(Icons.cloud_off_outlined, size: 18, color: AppColors.high),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              message,
              style: const TextStyle(fontSize: 12.5, height: 1.4),
            ),
          ),
        ],
      ),
    );
  }
}

// --------------------------------------------------------------------- card

class _ReportCard extends StatelessWidget {
  const _ReportCard({required this.report, required this.onTap});

  final Incident report;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    // Both leave the review path, so each is explained rather than tracked.
    final offPath = report.status == 'Rejected' || isMerged(report.status);

    return AppCard(
      onTap: onTap,
      accent: statusTone(report.status),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TypeBadge(report.type),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      report.title,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontSize: 15,
                        height: 1.3,
                        fontWeight: FontWeight.w600,
                        color: AppColors.ink,
                      ),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      [
                        report.type,
                        ?report.district,
                        timeAgo(report.reportedAt),
                      ].join(' · '),
                      style: const TextStyle(fontSize: 12.5, color: AppColors.body),
                    ),
                  ],
                ),
              ),
              const Icon(Icons.chevron_right, color: AppColors.body),
            ],
          ),
          const SizedBox(height: 14),
          if (offPath)
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(
                  isMerged(report.status) ? Icons.call_merge : Icons.block,
                  size: 16,
                  color: statusTone(report.status),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    statusExplainer(report.status),
                    style: const TextStyle(fontSize: 12.5, height: 1.4),
                  ),
                ),
              ],
            )
          else
            _ProgressTrack(status: report.status),
          const SizedBox(height: 12),
          _Pills(report: report),
        ],
      ),
    );
  }
}

/// The four review steps as a segmented bar, the current one labelled bold.
class _ProgressTrack extends StatelessWidget {
  const _ProgressTrack({required this.status});

  final String status;

  @override
  Widget build(BuildContext context) {
    final current = stepIndexFor(status);

    return Semantics(
      label: 'Progress: ${statusLabel(status)}',
      excludeSemantics: true,
      child: Row(
        children: [
          for (var i = 0; i < reportSteps.length; i++) ...[
            if (i > 0) const SizedBox(width: 4),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  AnimatedContainer(
                    duration: const Duration(milliseconds: 250),
                    height: 5,
                    decoration: BoxDecoration(
                      color: i <= current ? AppColors.brand : AppColors.border,
                      borderRadius: BorderRadius.circular(99),
                    ),
                  ),
                  const SizedBox(height: 6),
                  Text(
                    stepLabel(reportSteps[i]),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 10.5,
                      fontWeight: i == current ? FontWeight.w700 : FontWeight.w500,
                      color: i <= current ? AppColors.ink : AppColors.body,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _Pills extends StatelessWidget {
  const _Pills({required this.report});

  final Incident report;

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: 6,
      runSpacing: 6,
      children: [
        AppPill(statusLabel(report.status), tone: statusTone(report.status)),
        if (showsSeverity(report.status))
          AppPill(
            'Condition: ${report.severity}',
            tone: AppColors.forSeverity(report.severity),
          ),
        if (report.imageCount > 0) const AppPill('Photo attached'),
      ],
    );
  }
}

// ------------------------------------------------------------------- detail

/// Everything about one report, with its review as a timeline.
Future<void> showReportDetail(BuildContext context, Incident report) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    showDragHandle: true,
    backgroundColor: AppColors.surface,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
    ),
    builder: (sheetContext) => DraggableScrollableSheet(
      expand: false,
      initialChildSize: 0.8,
      minChildSize: 0.45,
      maxChildSize: 0.95,
      builder: (context, controller) => _ReportDetail(
        report: report,
        controller: controller,
      ),
    ),
  );
}

class _ReportDetail extends StatelessWidget {
  const _ReportDetail({required this.report, required this.controller});

  final Incident report;
  final ScrollController controller;

  @override
  Widget build(BuildContext context) {
    final sentAt = DateFormat('d MMM y, h:mm a').format(report.reportedAt);

    return ListView(
      controller: controller,
      padding: EdgeInsets.fromLTRB(
        20,
        0,
        20,
        24 + MediaQuery.paddingOf(context).bottom,
      ),
      children: [
        Row(
          children: [
            TypeBadge(report.type, size: 48),
            const SizedBox(width: 14),
            Expanded(
              child: Text(
                report.title,
                style: const TextStyle(
                  fontSize: 18,
                  height: 1.3,
                  fontWeight: FontWeight.w700,
                  color: AppColors.ink,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 12),
        _Pills(report: report),

        if (report.images.isNotEmpty) ...[
          const SizedBox(height: 16),
          IncidentPhotoGallery(photos: report.images, height: 200),
        ],

        const SizedBox(height: 24),
        const AppSectionTitle('Progress'),
        _Timeline(status: report.status),

        const SizedBox(height: 20),
        const AppSectionTitle('What you reported'),
        Text(
          report.description,
          style: const TextStyle(fontSize: 14, height: 1.5, color: AppColors.ink),
        ),

        const SizedBox(height: 24),
        const AppSectionTitle('Details'),
        AppCard(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 4),
          child: Column(
            children: [
              _Fact(icon: Icons.schedule, label: 'Sent', value: sentAt),
              _Fact(
                icon: Icons.location_city_outlined,
                label: 'District',
                value: report.district ?? 'Not given',
              ),
              if (report.addressText != null)
                _Fact(
                  icon: Icons.place_outlined,
                  label: 'Landmark',
                  value: report.addressText!,
                ),
              _Fact(
                icon: Icons.groups_outlined,
                label: 'People affected',
                value: report.estimatedAffectedPeople?.toString() ?? 'Not given',
              ),
              _Fact(
                icon: Icons.my_location,
                label: 'Location',
                value:
                    '${report.latitude.toStringAsFixed(5)}, '
                    '${report.longitude.toStringAsFixed(5)}',
                last: true,
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// The review path drawn top to bottom: done steps ticked, the current one
/// explained, later ones greyed. A rejected report ends after "Sent".
class _Timeline extends StatelessWidget {
  const _Timeline({required this.status});

  final String status;

  @override
  Widget build(BuildContext context) {
    final rejected = status == 'Rejected';
    final merged = isMerged(status);
    final steps = rejected
        ? const ['Reported', 'Rejected']
        : merged
        ? const ['Reported', 'Merged']
        : reportSteps;
    final current = rejected || merged ? 1 : stepIndexFor(status);

    return Column(
      children: [
        for (var i = 0; i < steps.length; i++)
          _TimelineStep(
            label: stepLabel(steps[i]),
            detail: i == current ? statusExplainer(steps[i]) : null,
            state: i < current
                ? _StepState.done
                : i == current
                ? _StepState.current
                : _StepState.pending,
            tone: rejected && i == current ? AppColors.critical : AppColors.brand,
            last: i == steps.length - 1,
          ),
      ],
    );
  }
}

enum _StepState { done, current, pending }

class _TimelineStep extends StatelessWidget {
  const _TimelineStep({
    required this.label,
    required this.detail,
    required this.state,
    required this.tone,
    required this.last,
  });

  final String label;
  final String? detail;
  final _StepState state;
  final Color tone;
  final bool last;

  @override
  Widget build(BuildContext context) {
    final reached = state != _StepState.pending;

    final Widget dot = switch (state) {
      _StepState.done => Container(
        width: 24,
        height: 24,
        decoration: BoxDecoration(color: tone, shape: BoxShape.circle),
        child: const Icon(Icons.check, size: 15, color: AppColors.brandInk),
      ),
      _StepState.current => Container(
        width: 24,
        height: 24,
        decoration: BoxDecoration(
          color: tone.withValues(alpha: 0.18),
          shape: BoxShape.circle,
          border: Border.all(color: tone, width: 2),
        ),
        child: Center(
          child: Container(
            width: 8,
            height: 8,
            decoration: BoxDecoration(color: tone, shape: BoxShape.circle),
          ),
        ),
      ),
      _StepState.pending => Container(
        width: 24,
        height: 24,
        decoration: BoxDecoration(
          color: AppColors.surface,
          shape: BoxShape.circle,
          border: Border.all(color: AppColors.border, width: 2),
        ),
      ),
    };

    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Column(
            children: [
              dot,
              if (!last)
                Expanded(
                  child: Container(
                    width: 2,
                    margin: const EdgeInsets.symmetric(vertical: 2),
                    color: state == _StepState.done ? tone : AppColors.border,
                  ),
                ),
            ],
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(top: 2, bottom: last ? 0 : 18),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    label,
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: reached ? FontWeight.w600 : FontWeight.w500,
                      color: reached ? AppColors.ink : AppColors.body,
                    ),
                  ),
                  if (detail != null && detail!.isNotEmpty) ...[
                    const SizedBox(height: 3),
                    Text(
                      detail!,
                      style: const TextStyle(fontSize: 12.5, height: 1.4),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Fact extends StatelessWidget {
  const _Fact({
    required this.icon,
    required this.label,
    required this.value,
    this.last = false,
  });

  final IconData icon;
  final String label;
  final String value;
  final bool last;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(vertical: 12),
      decoration: BoxDecoration(
        border: last
            ? null
            : const Border(bottom: BorderSide(color: AppColors.border)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: AppColors.body),
          const SizedBox(width: 12),
          SizedBox(
            width: 112,
            child: Text(label, style: const TextStyle(fontSize: 13)),
          ),
          Expanded(
            child: Text(
              value,
              style: const TextStyle(
                fontSize: 13,
                height: 1.35,
                fontWeight: FontWeight.w600,
                color: AppColors.ink,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
