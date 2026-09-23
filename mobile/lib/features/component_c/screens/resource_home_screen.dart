import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/widgets/app_ui.dart';
import '../services/resource_api.dart';

/// The Resources tab: ask the resource team for supplies, or offer some.
///
/// Two sections behind one segmented switch, both posting to
/// /api/resources/*. Styling comes from the shared kit, so this reads like the
/// disaster map and the report form.
class ResourceHomePage extends StatefulWidget {
  const ResourceHomePage({super.key});

  @override
  State<ResourceHomePage> createState() => _ResourceHomePageState();
}

class _ResourceHomePageState extends State<ResourceHomePage> {
  final _api = ResourceApi();

  int _section = 0;
  List<HelpRequest> _requests = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadRequests();
  }

  @override
  void dispose() {
    _api.dispose();
    super.dispose();
  }

  Future<void> _loadRequests() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final requests = await _api.getHelpRequests();
      if (mounted) setState(() => _requests = requests);
    } catch (error) {
      if (mounted) {
        setState(
          () => _error = error.toString().replaceFirst('Exception: ', ''),
        );
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'Resources',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
        actions: [
          IconButton(
            onPressed: _loading ? null : _loadRequests,
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh requests',
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          children: [
            if (_error != null)
              AppErrorBanner(message: _error!, onRetry: _loadRequests),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
              child: SegmentedButton<int>(
                segments: const [
                  ButtonSegment(
                    value: 0,
                    icon: Icon(Icons.volunteer_activism_outlined, size: 18),
                    label: Text('Resource request'),
                  ),
                  ButtonSegment(
                    value: 1,
                    icon: Icon(Icons.inventory_2_outlined, size: 18),
                    label: Text('Donate'),
                  ),
                ],
                selected: {_section},
                onSelectionChanged: (selection) =>
                    setState(() => _section = selection.first),
                style: SegmentedButton.styleFrom(
                  selectedBackgroundColor: AppColors.brand.withValues(
                    alpha: 0.18,
                  ),
                  selectedForegroundColor: AppColors.ink,
                  side: const BorderSide(color: AppColors.border),
                ),
              ),
            ),
            Expanded(
              child: IndexedStack(
                index: _section,
                children: [
                  RequestHelpPage(
                    api: _api,
                    requests: _requests,
                    loadingRequests: _loading,
                    onSubmitted: _loadRequests,
                  ),
                  DonatePage(api: _api),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Which colour a request's status earns. Pending is a caution, anything
/// settled reads as safe, a rejection as critical.
Color _statusTone(String status) {
  final value = status.toLowerCase();
  if (value.contains('reject') || value.contains('cancel')) {
    return AppColors.critical;
  }
  if (value.contains('pending') || value.contains('review')) {
    return AppColors.caution;
  }
  return AppColors.safe;
}

class RequestHelpPage extends StatefulWidget {
  const RequestHelpPage({
    required this.api,
    required this.requests,
    required this.loadingRequests,
    required this.onSubmitted,
    super.key,
  });

  final ResourceApi api;
  final List<HelpRequest> requests;
  final bool loadingRequests;
  final Future<void> Function() onSubmitted;

  @override
  State<RequestHelpPage> createState() => _RequestHelpPageState();
}

class _RequestHelpPageState extends State<RequestHelpPage> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _phone = TextEditingController();
  final _description = TextEditingController();

  String _needType = 'Food and water';
  bool _submitting = false;

  @override
  void dispose() {
    for (final controller in [_name, _phone, _description]) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    setState(() => _submitting = true);
    try {
      await widget.api.createHelpRequest(
        name: _name.text,
        phone: _phone.text,
        needType: _needType,
        description: _description.text,
      );
      if (!mounted) return;
      _description.clear();
      await widget.onSubmitted();
      _toast('Your request was sent to the resource manager.');
    } catch (error) {
      _toast(error.toString().replaceFirst('Exception: ', ''), isError: true);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  void _toast(String message, {bool isError = false}) =>
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(message),
          backgroundColor: isError ? AppColors.critical : null,
        ),
      );

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.gutter,
        4,
        AppSpacing.gutter,
        32,
      ),
      children: [
        const _SectionIntro(
          icon: Icons.health_and_safety_outlined,
          title: 'What do you need?',
          subtitle:
              'Tell the resource team what support is needed. Every request '
              'is reviewed by a resource manager.',
        ),
        const SizedBox(height: AppSpacing.gap),
        Form(
          key: _formKey,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _RequiredField(
                controller: _name,
                label: 'Your name',
                icon: Icons.person_outline,
              ),
              const SizedBox(height: AppSpacing.gap),
              _RequiredField(
                controller: _phone,
                label: 'Contact number',
                icon: Icons.phone_outlined,
                keyboardType: TextInputType.phone,
              ),
              const SizedBox(height: AppSpacing.gap),
              DropdownButtonFormField<String>(
                initialValue: _needType,
                decoration: const InputDecoration(
                  labelText: 'Type of help needed',
                  border: OutlineInputBorder(),
                  prefixIcon: Icon(Icons.category_outlined),
                ),
                items:
                    const [
                          'Food and water',
                          'Medical aid',
                          'Rescue',
                          'Shelter',
                          'Other',
                        ]
                        .map(
                          (value) => DropdownMenuItem(
                            value: value,
                            child: Text(value),
                          ),
                        )
                        .toList(),
                onChanged: (value) => setState(() => _needType = value!),
              ),
              const SizedBox(height: AppSpacing.gap),
              TextFormField(
                controller: _description,
                maxLines: 4,
                decoration: const InputDecoration(
                  labelText: 'Describe what is needed',
                  alignLabelWithHint: true,
                  border: OutlineInputBorder(),
                  prefixIcon: Icon(Icons.notes_outlined),
                ),
                validator: (value) => (value?.trim() ?? '').isEmpty
                    ? 'Please describe the need.'
                    : null,
              ),
              const SizedBox(height: 18),
              AppPrimaryButton(
                label: _submitting ? 'Sending request…' : 'Send request',
                icon: Icons.send,
                busy: _submitting,
                onPressed: _submit,
              ),
            ],
          ),
        ),
        const SizedBox(height: 28),
        const AppSectionTitle('Your requests'),
        if (widget.loadingRequests)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 24),
            child: Center(child: CircularProgressIndicator()),
          )
        else if (widget.requests.isEmpty)
          const AppEmptyState(
            icon: Icons.inbox_outlined,
            title: 'No requests yet',
            message: 'Requests you send appear here with their latest status.',
          )
        else
          for (final request in widget.requests)
            Padding(
              padding: const EdgeInsets.only(bottom: AppSpacing.gap),
              child: AppCard(
                accent: _statusTone(request.status),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            request.needType,
                            style: const TextStyle(
                              fontSize: 14.5,
                              fontWeight: FontWeight.w700,
                              color: AppColors.ink,
                            ),
                          ),
                        ),
                        AppPill(
                          request.status,
                          tone: _statusTone(request.status),
                        ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Text(
                      request.description,
                      style: const TextStyle(fontSize: 13, height: 1.4),
                    ),
                  ],
                ),
              ),
            ),
      ],
    );
  }
}

class DonatePage extends StatefulWidget {
  const DonatePage({required this.api, super.key});

  final ResourceApi api;

  @override
  State<DonatePage> createState() => _DonatePageState();
}

class _DonatePageState extends State<DonatePage> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _phone = TextEditingController();
  final _quantity = TextEditingController();
  final _unit = TextEditingController();
  final _notes = TextEditingController();

  String _donationType = 'Food and water';
  bool _submitting = false;

  @override
  void dispose() {
    for (final controller in [_name, _phone, _quantity, _unit, _notes]) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    setState(() => _submitting = true);
    try {
      await widget.api.createDonation(
        name: _name.text,
        phone: _phone.text,
        donationType: _donationType,
        quantity: double.parse(_quantity.text),
        unit: _unit.text,
        notes: _notes.text,
      );
      if (!mounted) return;
      _quantity.clear();
      _unit.clear();
      _notes.clear();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Thank you. The resource manager will contact you.'),
        ),
      );
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(error.toString().replaceFirst('Exception: ', '')),
          backgroundColor: AppColors.critical,
        ),
      );
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.gutter,
        4,
        AppSpacing.gutter,
        32,
      ),
      children: [
        const _SectionIntro(
          icon: Icons.volunteer_activism_outlined,
          title: 'Give what you can',
          subtitle:
              'Offer food, water, medical supplies or other resources. A '
              'resource manager reviews every offer and contacts you.',
        ),
        const SizedBox(height: AppSpacing.gap),
        Form(
          key: _formKey,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _RequiredField(
                controller: _name,
                label: 'Your name',
                icon: Icons.person_outline,
              ),
              const SizedBox(height: AppSpacing.gap),
              _RequiredField(
                controller: _phone,
                label: 'Contact number',
                icon: Icons.phone_outlined,
                keyboardType: TextInputType.phone,
              ),
              const SizedBox(height: AppSpacing.gap),
              DropdownButtonFormField<String>(
                initialValue: _donationType,
                decoration: const InputDecoration(
                  labelText: 'What are you donating?',
                  border: OutlineInputBorder(),
                  prefixIcon: Icon(Icons.category_outlined),
                ),
                items:
                    const [
                          'Food and water',
                          'Medical supplies',
                          'Clothing',
                          'Other',
                        ]
                        .map(
                          (value) => DropdownMenuItem(
                            value: value,
                            child: Text(value),
                          ),
                        )
                        .toList(),
                onChanged: (value) => setState(() => _donationType = value!),
              ),
              const SizedBox(height: AppSpacing.gap),
              Row(
                children: [
                  Expanded(
                    child: _RequiredField(
                      controller: _quantity,
                      label: 'Quantity',
                      icon: Icons.numbers,
                      keyboardType: const TextInputType.numberWithOptions(
                        decimal: true,
                      ),
                    ),
                  ),
                  const SizedBox(width: AppSpacing.gap),
                  Expanded(
                    child: _RequiredField(
                      controller: _unit,
                      label: 'Unit (kg, boxes…)',
                      icon: Icons.straighten,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.gap),
              TextFormField(
                controller: _notes,
                maxLines: 3,
                decoration: const InputDecoration(
                  labelText: 'Notes (optional)',
                  alignLabelWithHint: true,
                  border: OutlineInputBorder(),
                  prefixIcon: Icon(Icons.notes_outlined),
                ),
              ),
              const SizedBox(height: 18),
              AppPrimaryButton(
                label: _submitting ? 'Sending donation…' : 'Offer donation',
                icon: Icons.volunteer_activism,
                busy: _submitting,
                onPressed: _submit,
              ),
            ],
          ),
        ),
        const SizedBox(height: AppSpacing.gap),
        const AppCard(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(Icons.info_outline, size: 18, color: AppColors.body),
              SizedBox(width: 10),
              Expanded(
                child: Text(
                  'Your offer goes to the resource manager for review and '
                  'coordination.',
                  style: TextStyle(fontSize: 13, height: 1.4),
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// The heading each section opens with: icon, title, one line of context.
class _SectionIntro extends StatelessWidget {
  const _SectionIntro({
    required this.icon,
    required this.title,
    required this.subtitle,
  });

  final IconData icon;
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Container(
              padding: const EdgeInsets.all(8),
              decoration: BoxDecoration(
                color: AppColors.brand.withValues(alpha: 0.14),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Icon(icon, size: 20, color: AppColors.brandInk),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                title,
                style: const TextStyle(
                  fontSize: 19,
                  fontWeight: FontWeight.w700,
                  color: AppColors.ink,
                  letterSpacing: -0.2,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 8),
        Text(
          subtitle,
          style: const TextStyle(
            fontSize: 13,
            height: 1.45,
            color: AppColors.body,
          ),
        ),
      ],
    );
  }
}

/// A required text field, styled like the report form's.
class _RequiredField extends StatelessWidget {
  const _RequiredField({
    required this.controller,
    required this.label,
    required this.icon,
    this.keyboardType,
  });

  final TextEditingController controller;
  final String label;
  final IconData icon;
  final TextInputType? keyboardType;

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      controller: controller,
      keyboardType: keyboardType,
      decoration: InputDecoration(
        labelText: label,
        border: const OutlineInputBorder(),
        prefixIcon: Icon(icon),
      ),
      validator: (value) =>
          (value?.trim() ?? '').isEmpty ? 'This field is required.' : null,
    );
  }
}
