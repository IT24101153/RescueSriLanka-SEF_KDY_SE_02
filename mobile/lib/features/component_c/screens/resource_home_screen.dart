import 'dart:async';

import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/models/auth.dart';
import '../../../shared/services/auth_service.dart';
import '../../../shared/widgets/app_ui.dart';
import '../services/resource_api.dart';

/// The Resources tab: ask the resource team for supplies, or offer some.
///
/// Two sections behind one segmented switch, both posting to
/// /api/resources/*. Styling comes from the shared kit, so this reads like the
/// disaster map and the report form.
class ResourceHomePage extends StatefulWidget {
  const ResourceHomePage({super.key, this.auth});

  final AuthService? auth;

  @override
  State<ResourceHomePage> createState() => _ResourceHomePageState();
}

class _ResourceHomePageState extends State<ResourceHomePage> {
  late final ResourceApi _api;

  int _section = 0;
  List<HelpRequest> _requests = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _api = ResourceApi(token: widget.auth?.token);
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
      appBar: AppHeader(title: 'Resources', loading: _loading),
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
                    user: widget.auth?.user,
                    requests: _requests,
                    loadingRequests: _loading,
                    onSubmitted: _loadRequests,
                    onRefresh: _loadRequests,
                  ),
                  DonatePage(
                    api: _api,
                    user: widget.auth?.user,
                    onRefresh: _loadRequests,
                  ),
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

const _resourceCategories = <String, List<String>>{
  'Food': ['Dry foods', 'Rice', 'Other'],
  'Water': ['Bottled water', 'Drinking water', 'Water containers', 'Other'],
  'Medical': [
    'Bandages',
    'Plasters',
    'Surgical spirits',
    'Saline',
    'Gauze / cotton packets',
    'Other',
  ],
  'Sanitary products': ['Napkins', 'Other'],
  'Hygiene items': ['Soap', 'Toothpaste', 'Toothbrushes', 'Other'],
  'Other': ['Other'],
};

class _ResourceItemDraft {
  String category = 'Food';
  String item = 'Dry foods';
  final customItem = TextEditingController();
  final quantity = TextEditingController();
  final unit = TextEditingController();

  bool get needsCustomItem => category == 'Other' || item == 'Other';

  String get itemName => needsCustomItem
      ? customItem.text.trim()
      : item;

  ResourceSubmissionItem toSubmissionItem() => ResourceSubmissionItem(
    category: category,
    itemName: itemName,
    quantity: double.parse(quantity.text),
    unit: unit.text.trim(),
  );

  void dispose() {
    customItem.dispose();
    quantity.dispose();
    unit.dispose();
  }
}

class _ResourceItemEditor extends StatelessWidget {
  const _ResourceItemEditor({
    required this.item,
    required this.index,
    required this.canRemove,
    required this.onChanged,
    required this.onRemove,
    super.key,
  });

  final _ResourceItemDraft item;
  final int index;
  final bool canRemove;
  final VoidCallback onChanged;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final subcategories =
        _resourceCategories[item.category] ?? const <String>[];
    return Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.gap),
      child: AppCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    'Item ${index + 1}',
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                ),
                if (canRemove)
                  IconButton(
                    tooltip: 'Remove item',
                    onPressed: onRemove,
                    icon: const Icon(Icons.remove_circle_outline),
                  ),
              ],
            ),
            DropdownButtonFormField<String>(
              key: ValueKey(
                'category-${identityHashCode(item)}-${item.category}',
              ),
              initialValue: item.category,
              decoration: const InputDecoration(
                labelText: 'Category',
                border: OutlineInputBorder(),
                prefixIcon: Icon(Icons.category_outlined),
              ),
              items: _resourceCategories.keys
                  .map(
                    (category) => DropdownMenuItem(
                      value: category,
                      child: Text(category),
                    ),
                  )
                  .toList(),
              onChanged: (category) {
                if (category == null) return;
                item.category = category;
                item.item = _resourceCategories[category]?.first ?? '';
                item.customItem.clear();
                onChanged();
              },
            ),
            if (subcategories.isNotEmpty) ...[
              const SizedBox(height: AppSpacing.gap),
              DropdownButtonFormField<String>(
                key: ValueKey(
                  'item-${identityHashCode(item)}-${item.category}-${item.item}',
                ),
                initialValue: item.item,
                decoration: const InputDecoration(
                  labelText: 'Item',
                  border: OutlineInputBorder(),
                  prefixIcon: Icon(Icons.inventory_2_outlined),
                ),
                items: subcategories
                    .map(
                      (subcategory) => DropdownMenuItem(
                        value: subcategory,
                        child: Text(subcategory),
                      ),
                    )
                    .toList(),
                onChanged: (subcategory) {
                  if (subcategory == null) return;
                  item.item = subcategory;
                  item.customItem.clear();
                  onChanged();
                },
              ),
            ],
            if (item.needsCustomItem) ...[
              const SizedBox(height: AppSpacing.gap),
              TextFormField(
                controller: item.customItem,
                maxLength: 100,
                decoration: const InputDecoration(
                  labelText: 'Specify item',
                  border: OutlineInputBorder(),
                  prefixIcon: Icon(Icons.edit_outlined),
                ),
                validator: (value) => (value?.trim() ?? '').isEmpty
                    ? 'Enter the item name.'
                    : null,
              ),
            ],
            const SizedBox(height: AppSpacing.gap),
            Row(
              children: [
                Expanded(
                  child: TextFormField(
                    controller: item.quantity,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    decoration: const InputDecoration(
                      labelText: 'Quantity',
                      border: OutlineInputBorder(),
                      prefixIcon: Icon(Icons.numbers),
                    ),
                    validator: (value) {
                      final quantity = double.tryParse(value?.trim() ?? '');
                      return quantity == null || quantity <= 0
                          ? 'Enter a positive quantity.'
                          : null;
                    },
                  ),
                ),
                const SizedBox(width: AppSpacing.gap),
                Expanded(
                  child: TextFormField(
                    controller: item.unit,
                    decoration: const InputDecoration(
                      labelText: 'Unit',
                      hintText: 'kg, packs, boxes',
                      border: OutlineInputBorder(),
                      prefixIcon: Icon(Icons.straighten),
                    ),
                    validator: (value) =>
                        (value?.trim() ?? '').isEmpty ? 'Enter a unit.' : null,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class RequestHelpPage extends StatefulWidget {
  const RequestHelpPage({
    required this.api,
    this.user,
    required this.requests,
    required this.loadingRequests,
    required this.onSubmitted,
    required this.onRefresh,
    super.key,
  });

  final ResourceApi api;
  final AuthUser? user;
  final List<HelpRequest> requests;
  final bool loadingRequests;
  final Future<void> Function() onSubmitted;

  /// Pull-to-refresh on the list reloads the citizen's requests.
  final Future<void> Function() onRefresh;

  @override
  State<RequestHelpPage> createState() => _RequestHelpPageState();
}

class _RequestHelpPageState extends State<RequestHelpPage> {
  final _formKey = GlobalKey<FormState>();
  final List<_ResourceItemDraft> _items = [_ResourceItemDraft()];
  bool _submitting = false;

  @override
  void dispose() {
    for (final item in _items) {
      item.dispose();
    }
    super.dispose();
  }

  void _addItem() => setState(() => _items.add(_ResourceItemDraft()));

  void _removeItem(_ResourceItemDraft item) {
    if (_items.length < 2) return;
    setState(() {
      _items.remove(item);
      item.dispose();
    });
  }

  Future<void> _submit() async {
    final user = widget.user;
    if (user == null || (user.phoneNumber?.trim().isEmpty ?? true)) {
      _toast(
        'Add your phone number in your profile before sending a request.',
        isError: true,
      );
      return;
    }
    if (!(_formKey.currentState?.validate() ?? false)) return;

    setState(() => _submitting = true);
    try {
      await widget.api.createHelpRequestsBatch(
        items: _items.map((item) => item.toSubmissionItem()).toList(),
      );
      if (!mounted) return;
      for (final item in _items) {
        item.dispose();
      }
      setState(
        () => _items
          ..clear()
          ..add(_ResourceItemDraft()),
      );
      await widget.onSubmitted();
      _toast('Your items were sent to the resource manager.');
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
    return RefreshIndicator(
      onRefresh: widget.onRefresh,
      child: _buildList(context),
    );
  }

  Widget _buildList(BuildContext context) {
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
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
        _AccountDetails(user: widget.user),
        const SizedBox(height: AppSpacing.gap),
        Form(
          key: _formKey,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              for (var index = 0; index < _items.length; index++)
                _ResourceItemEditor(
                  key: ObjectKey(_items[index]),
                  item: _items[index],
                  index: index,
                  canRemove: _items.length > 1,
                  onChanged: () => setState(() {}),
                  onRemove: () => _removeItem(_items[index]),
                ),
              OutlinedButton.icon(
                onPressed: _items.length >= 20 ? null : _addItem,
                icon: const Icon(Icons.add),
                label: const Text('Add another item'),
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
  const DonatePage({
    required this.api,
    this.user,
    required this.onRefresh,
    super.key,
  });

  final ResourceApi api;
  final AuthUser? user;

  /// Pull-to-refresh reloads the section, as it does on the request tab.
  final Future<void> Function() onRefresh;

  @override
  State<DonatePage> createState() => _DonatePageState();
}

class _DonatePageState extends State<DonatePage> {
  final _formKey = GlobalKey<FormState>();
  final _notes = TextEditingController();
  final List<_ResourceItemDraft> _items = [_ResourceItemDraft()];
  bool _submitting = false;
  bool _loadingDonations = true;
  String? _donationError;
  List<Donation> _donations = [];
  Timer? _donationRefreshTimer;

  @override
  void initState() {
    super.initState();
    _loadDonations();
    _donationRefreshTimer = Timer.periodic(
      const Duration(seconds: 30),
      (_) => _loadDonations(showLoading: false),
    );
  }

  Future<void> _loadDonations({bool showLoading = true}) async {
    if (showLoading && mounted) {
      setState(() {
        _loadingDonations = true;
        _donationError = null;
      });
    }
    try {
      final donations = await widget.api.getDonations();
      if (!mounted) return;
      setState(() {
        _donations = donations
            .where((donation) => donation.userId == widget.user?.id)
            .toList();
        _donationError = null;
      });
    } catch (error) {
      if (mounted) {
        setState(
          () => _donationError = error.toString().replaceFirst('Exception: ', ''),
        );
      }
    } finally {
      if (mounted && showLoading) setState(() => _loadingDonations = false);
    }
  }

  Future<void> _refreshDonations() async {
    await Future.wait([widget.onRefresh(), _loadDonations()]);
  }

  void _addItem() => setState(() => _items.add(_ResourceItemDraft()));

  void _removeItem(_ResourceItemDraft item) {
    if (_items.length < 2) return;
    setState(() {
      _items.remove(item);
      item.dispose();
    });
  }

  @override
  void dispose() {
    _donationRefreshTimer?.cancel();
    for (final item in _items) {
      item.dispose();
    }
    _notes.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final user = widget.user;
    if (user == null || (user.phoneNumber?.trim().isEmpty ?? true)) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Add your phone number in your profile before donating.',
          ),
          backgroundColor: AppColors.critical,
        ),
      );
      return;
    }
    if (!(_formKey.currentState?.validate() ?? false)) return;

    setState(() => _submitting = true);
    try {
      await widget.api.createDonationsBatch(
        items: _items.map((item) => item.toSubmissionItem()).toList(),
        notes: _notes.text,
      );
      if (!mounted) return;
      await _loadDonations();
      if (!mounted) return;
      _notes.clear();
      for (final item in _items) {
        item.dispose();
      }
      setState(
        () => _items
          ..clear()
          ..add(_ResourceItemDraft()),
      );
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
    return RefreshIndicator(
      onRefresh: _refreshDonations,
      child: _buildList(context),
    );
  }

  Widget _buildList(BuildContext context) {
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
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
        _AccountDetails(user: widget.user),
        const SizedBox(height: AppSpacing.gap),
        Form(
          key: _formKey,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              for (var index = 0; index < _items.length; index++)
                _ResourceItemEditor(
                  key: ObjectKey(_items[index]),
                  item: _items[index],
                  index: index,
                  canRemove: _items.length > 1,
                  onChanged: () => setState(() {}),
                  onRemove: () => _removeItem(_items[index]),
                ),
              OutlinedButton.icon(
                onPressed: _items.length >= 20 ? null : _addItem,
                icon: const Icon(Icons.add),
                label: const Text('Add another item'),
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
        const SizedBox(height: AppSpacing.gap),
        const AppSectionTitle('Your donations'),
        if (_loadingDonations)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 24),
            child: Center(child: CircularProgressIndicator()),
          )
        else if (_donationError != null)
          AppErrorBanner(
            message: _donationError!,
            onRetry: _loadDonations,
          )
        else if (_donations.isEmpty)
          const AppEmptyState(
            icon: Icons.volunteer_activism_outlined,
            title: 'No donations yet',
            message: 'Donations you offer appear here with their latest status.',
          )
        else
          for (final donation in _donations)
            Padding(
              padding: const EdgeInsets.only(bottom: AppSpacing.gap),
              child: AppCard(
                accent: _statusTone(donation.status),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            donation.donationType,
                            style: const TextStyle(
                              fontSize: 14.5,
                              fontWeight: FontWeight.w700,
                              color: AppColors.ink,
                            ),
                          ),
                          const SizedBox(height: 5),
                          Text(
                            '${donation.quantity} ${donation.unit} · ${MaterialLocalizations.of(context).formatShortDate(donation.createdAt.toLocal())}',
                            style: const TextStyle(fontSize: 12.5, color: AppColors.body),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(width: 10),
                    AppPill(
                      _donationStatusLabel(donation.status),
                      tone: _statusTone(donation.status),
                    ),
                  ],
                ),
              ),
            ),
      ],
    );
  }
}

String _donationStatusLabel(String status) => switch (status.toLowerCase()) {
  'pendingreview' || 'pending' => 'Pending',
  'accepted' => 'Accepted',
  'rejected' => 'Rejected',
  _ => status,
};

class _AccountDetails extends StatelessWidget {
  const _AccountDetails({required this.user});

  final AuthUser? user;

  @override
  Widget build(BuildContext context) {
    final account = user;
    final phone = account?.phoneNumber?.trim();

    return AppCard(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(Icons.account_circle_outlined, color: AppColors.brandInk),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  account?.fullName ?? 'Account details unavailable',
                  style: const TextStyle(
                    fontWeight: FontWeight.w700,
                    color: AppColors.ink,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  phone?.isNotEmpty == true
                      ? phone!
                      : 'Add a phone number in Profile',
                  style: const TextStyle(fontSize: 13, color: AppColors.body),
                ),
                Text(
                  'District: ${account?.district ?? 'Not set'}',
                  style: const TextStyle(fontSize: 13, color: AppColors.body),
                ),
              ],
            ),
          ),
        ],
      ),
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
