import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/widgets/app_ui.dart';
import '../help_style.dart';
import '../services/help_request_service.dart';
import '../widgets/emergency_contacts_section.dart';
import '../widgets/guidance_section.dart';
import '../widgets/estimated_people_field.dart';

class MyRequestsScreen extends StatefulWidget {
  const MyRequestsScreen({super.key});

  @override
  State<MyRequestsScreen> createState() => _MyRequestsScreenState();
}

class _MyRequestsScreenState extends State<MyRequestsScreen> {
  List<HelpRequest> _requests = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    final loadResult = await HelpRequestService.getMineWithStatus();
    final data = loadResult.requests;
    if (!mounted) return;
    setState(() {
      _requests = data;
      _error = loadResult.error;
      _loading = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'My requests',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh',
            onPressed: _loading ? null : _load,
          ),
        ],
      ),
      body: SafeArea(
        child: RefreshIndicator(onRefresh: _load, child: _body()),
      ),
    );
  }

  Widget _body() {
    if (_loading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (_error != null) {
      return ListView(
        children: [
          AppEmptyState(
            icon: Icons.cloud_off_outlined,
            title: 'Unable to load your requests',
            message: _error,
            action: OutlinedButton.icon(
              onPressed: _load,
              icon: const Icon(Icons.refresh, size: 18),
              label: const Text('Try again'),
            ),
          ),
        ],
      );
    }

    if (_requests.isEmpty) {
      return ListView(
        children: const [
          AppEmptyState(
            icon: Icons.checklist_outlined,
            title: 'No requests yet',
            message:
                'Requests you submit appear here with their latest status.',
          ),
        ],
      );
    }

    return ListView.separated(
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.gutter,
        16,
        AppSpacing.gutter,
        28,
      ),
      itemCount: _requests.length,
      separatorBuilder: (_, _) => const SizedBox(height: AppSpacing.gap),
      itemBuilder: (context, index) {
        final request = _requests[index];

        return AppCard(
          accent: helpStatusTone(request.status),
          onTap: () async {
            final changed = await Navigator.of(context).push<bool>(
              MaterialPageRoute(
                builder: (_) => _RequestDetailScreen(request: request),
              ),
            );
            if (changed == true) _load();
          },
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(helpTypeIcon(request.type), size: 19, color: AppColors.body),
              const SizedBox(width: 11),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      helpRequestTypeLabels[request.type],
                      style: const TextStyle(
                        fontWeight: FontWeight.w700,
                        fontSize: 14.5,
                        color: AppColors.ink,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      request.description,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        color: AppColors.body,
                        fontSize: 13,
                        height: 1.35,
                      ),
                    ),
                    Text('People affected: ${request.estimatedPeopleCount ?? 'Not specified'}'),
                  ],
                ),
              ),
              const SizedBox(width: 10),
              AppPill(
                helpRequestStatusLabels[request.status],
                tone: helpStatusTone(request.status),
              ),
            ],
          ),
        );
      },
    );
  }
}

class _RequestDetailScreen extends StatefulWidget {
  const _RequestDetailScreen({required this.request});

  final HelpRequest request;

  @override
  State<_RequestDetailScreen> createState() => _RequestDetailScreenState();
}

class _RequestDetailScreenState extends State<_RequestDetailScreen> {
  List<StatusHistoryEntry> _history = [];
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final data = await HelpRequestService.getHistory(widget.request.id);
    if (!mounted) return;
    setState(() {
      _history = data;
      _loading = false;
    });
  }

  bool get _canEditOrCancel =>
      widget.request.status == 0 && widget.request.verificationStatus == 0;

  Future<void> _editRequest() async {
    var type = widget.request.type;
    final description = TextEditingController(text: widget.request.description);
    final people = TextEditingController(text: widget.request.estimatedPeopleCount?.toString() ?? '');
    final peopleFormKey = GlobalKey<FormState>();
    final changed = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: const Text('Edit request'),
          scrollable: true,
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              DropdownButtonFormField<int>(
                initialValue: type,
                items: List.generate(
                  helpRequestTypeLabels.length,
                  (index) => DropdownMenuItem(
                    value: index,
                    child: Text(helpRequestTypeLabels[index]),
                  ),
                ),
                onChanged: (value) => setDialogState(() => type = value!),
                decoration: const InputDecoration(labelText: 'Type'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: description,
                minLines: 3,
                maxLines: 6,
                maxLength: 2000,
                decoration: const InputDecoration(labelText: 'Description'),
              ),
              Form(key: peopleFormKey, child: EstimatedPeopleField(controller: people)),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () async {
                if (description.text.trim().length < 5) return;
                if (!peopleFormKey.currentState!.validate()) return;
                final saved = await HelpRequestService.update(
                  request: widget.request,
                  type: type,
                  description: description.text.trim(),
                  estimatedPeopleCount: int.parse(people.text),
                );
                if (context.mounted) Navigator.pop(context, saved);
              },
              child: const Text('Save'),
            ),
          ],
        ),
      ),
    );
    description.dispose();
    people.dispose();
    if (!mounted || changed != true) return;
    Navigator.pop(context, true);
  }

  Future<void> _cancelRequest() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Cancel this request?'),
        content: const Text(
          'This cannot be undone. Coordinators will retain the cancellation in the request history.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Keep request'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Cancel request'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
    final cancelled = await HelpRequestService.cancel(widget.request.id);
    if (!mounted) return;
    if (cancelled) {
      Navigator.pop(context, true);
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Could not cancel this request. Please try again.'),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final request = widget.request;

    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: Text(
          helpRequestTypeLabels[request.type],
          style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.gutter,
            18,
            AppSpacing.gutter,
            28,
          ),
          children: [
            if (request.imageUrl != null) ...[
              ClipRRect(
                borderRadius: AppSpacing.radius,
                child: Image.network(
                  request.imageUrl!,
                  height: 200,
                  width: double.infinity,
                  fit: BoxFit.cover,
                  errorBuilder: (_, _, _) => const SizedBox(
                    height: 100,
                    child: Center(
                      child: Text('Unable to load submitted photo'),
                    ),
                  ),
                ),
              ),
              const SizedBox(height: 16),
            ],
            AppCard(
              accent: helpStatusTone(request.status),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      AppPill(
                        helpRequestStatusLabels[request.status],
                        tone: helpStatusTone(request.status),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          verificationStatusLabels[request.verificationStatus],
                          style: const TextStyle(
                            fontSize: 12,
                            color: AppColors.body,
                          ),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  Text(
                    request.description,
                    style: const TextStyle(fontSize: 14.5, height: 1.45),
                  ),
                  Text('People affected: ${request.estimatedPeopleCount ?? 'Not specified'}'),
                  if (request.verificationNotes != null &&
                      request.verificationNotes!.isNotEmpty) ...[
                    const SizedBox(height: 10),
                    Text(
                      'Coordinator note: ${request.verificationNotes!}',
                      style: const TextStyle(
                        fontSize: 12.5,
                        height: 1.4,
                        color: AppColors.body,
                      ),
                    ),
                  ],
                ],
              ),
            ),
            if (_canEditOrCancel) ...[
              const SizedBox(height: 14),
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: _editRequest,
                      icon: const Icon(Icons.edit_outlined),
                      label: const Text('Edit request'),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: _cancelRequest,
                      icon: const Icon(Icons.cancel_outlined),
                      label: const Text('Cancel request'),
                    ),
                  ),
                ],
              ),
            ],
            const SizedBox(height: 24),
            const AppSectionTitle('Messages from the response team'),
            GuidanceSection(requestId: request.id),
            const SizedBox(height: 24),
            const AppSectionTitle('Emergency contacts for your area'),
            EmergencyContactsSection(
              load: () =>
                  HelpRequestService.getRequestEmergencyContacts(request.id),
            ),
            const SizedBox(height: 24),
            const AppSectionTitle('Status history'),
            if (_loading)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 20),
                child: Center(child: CircularProgressIndicator()),
              ),
            if (!_loading && _history.isEmpty)
              const Text(
                'No changes recorded yet.',
                style: TextStyle(color: AppColors.body, fontSize: 13),
              ),
            ..._history.map(
              (entry) => Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Container(
                      margin: const EdgeInsets.only(top: 5, right: 10),
                      width: 8,
                      height: 8,
                      decoration: BoxDecoration(
                        color: helpStatusTone(entry.newStatus),
                        shape: BoxShape.circle,
                      ),
                    ),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '${helpRequestStatusLabels[entry.oldStatus]} → '
                            '${helpRequestStatusLabels[entry.newStatus]}',
                            style: const TextStyle(
                              fontWeight: FontWeight.w600,
                              fontSize: 13,
                              color: AppColors.ink,
                            ),
                          ),
                          if (entry.notes != null)
                            Text(
                              entry.notes!,
                              style: const TextStyle(
                                color: AppColors.body,
                                fontSize: 12,
                                height: 1.35,
                              ),
                            ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
