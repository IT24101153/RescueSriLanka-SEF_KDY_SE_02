import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/widgets/app_ui.dart';
import '../services/help_request_service.dart';

/// What the response team has told the citizen about one request: notes and
/// Do / Don't safety advice, urgent ones first. Every Do and Don't carries an
/// icon as well as its colour, so neither is the only cue.
class GuidanceSection extends StatefulWidget {
  const GuidanceSection({super.key, required this.requestId});

  final String requestId;

  @override
  State<GuidanceSection> createState() => _GuidanceSectionState();
}

class _GuidanceSectionState extends State<GuidanceSection> {
  List<HelpRequestMessage>? _messages;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    final result = await HelpRequestService.getMessages(widget.requestId);
    if (!mounted) return;
    setState(() {
      _messages = result;
      _loading = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 20),
        child: Center(child: CircularProgressIndicator()),
      );
    }

    final messages = _messages;
    if (messages == null) {
      return AppErrorBanner(
        message: 'Could not load messages from the response team.',
        onRetry: _load,
      );
    }
    if (messages.isEmpty) {
      return const Text(
        'No messages from the response team yet. Safety advice will appear '
        'here, and you will be notified.',
        style: TextStyle(color: AppColors.body, fontSize: 13, height: 1.4),
      );
    }

    return Column(
      children: [
        for (final message in messages) ...[
          _MessageCard(message: message),
          const SizedBox(height: AppSpacing.gap),
        ],
      ],
    );
  }
}

class _MessageCard extends StatelessWidget {
  const _MessageCard({required this.message});

  final HelpRequestMessage message;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      accent: message.isCritical ? AppColors.danger : null,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              if (message.isCritical) ...[
                const AppPill('Urgent', tone: AppColors.danger),
                const SizedBox(width: 8),
              ],
              Text(
                DateFormat('d MMM, HH:mm').format(message.createdAt),
                style: const TextStyle(fontSize: 12, color: AppColors.body),
              ),
            ],
          ),
          if (message.message != null) ...[
            const SizedBox(height: 8),
            Text(
              message.message!,
              style: const TextStyle(fontSize: 14.5, height: 1.45),
            ),
          ],
          if (message.doItems.isNotEmpty) ...[
            const SizedBox(height: 12),
            _ItemList(
              title: 'Do',
              items: message.doItems,
              icon: Icons.check_circle_outline,
              tone: AppColors.safe,
            ),
          ],
          if (message.dontItems.isNotEmpty) ...[
            const SizedBox(height: 12),
            _ItemList(
              title: "Don't",
              items: message.dontItems,
              icon: Icons.do_not_disturb_on_outlined,
              tone: AppColors.danger,
            ),
          ],
        ],
      ),
    );
  }
}

class _ItemList extends StatelessWidget {
  const _ItemList({
    required this.title,
    required this.items,
    required this.icon,
    required this.tone,
  });

  final String title;
  final List<String> items;
  final IconData icon;
  final Color tone;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          title,
          style: TextStyle(
            fontSize: 12.5,
            fontWeight: FontWeight.w700,
            color: tone,
          ),
        ),
        const SizedBox(height: 4),
        for (final item in items)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(icon, size: 17, color: tone),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    item,
                    style: const TextStyle(fontSize: 14, height: 1.4),
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }
}
