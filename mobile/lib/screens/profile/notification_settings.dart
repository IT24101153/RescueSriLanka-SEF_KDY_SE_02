import 'package:flutter/material.dart';

import '../../core/theme.dart';
import '../../services/api_client.dart';
import '../../services/auth_service.dart';

/// Profile → Notification settings.
///
/// Two decisions live here: which district's warnings you want, and whether you
/// want email at all. A citizen who has set a district is emailed whenever a
/// coordinator confirms a High or Critical disaster there — which makes this
/// screen the subscription, and worth being explicit about what it does.
class NotificationSettings extends StatefulWidget {
  const NotificationSettings({super.key, required this.auth});

  final AuthService auth;

  @override
  State<NotificationSettings> createState() => _NotificationSettingsState();
}

class _NotificationSettingsState extends State<NotificationSettings> {
  final ApiClient _api = ApiClient.anonymous();

  List<String> _districts = const [];
  String? _district;
  bool _emailsOn = true;

  bool _loading = true;
  bool _saving = false;
  String? _loadError;

  @override
  void initState() {
    super.initState();

    final user = widget.auth.user;
    _district = user?.district;
    _emailsOn = user?.emailNotificationsEnabled ?? true;

    _loadDistricts();
  }

  @override
  void dispose() {
    _api.dispose();
    super.dispose();
  }

  Future<void> _loadDistricts() async {
    try {
      final districts = await _api.fetchDistricts();
      if (!mounted) return;
      setState(() {
        _districts = districts;
        _loading = false;
      });
    } on ApiException catch (error) {
      if (!mounted) return;
      setState(() {
        _loadError = error.message;
        _loading = false;
      });
    }
  }

  /// True when the form differs from what the server last told us.
  bool get _dirty {
    final user = widget.auth.user;
    return _district != user?.district ||
        _emailsOn != (user?.emailNotificationsEnabled ?? true);
  }

  Future<void> _save() async {
    setState(() => _saving = true);

    final result = await widget.auth.updatePreferences(
      district: _district,
      // A null district means "leave it alone" to the API, so clearing it has
      // to be said out loud.
      clearDistrict: _district == null,
      emailNotificationsEnabled: _emailsOn,
    );

    if (!mounted) return;
    setState(() => _saving = false);

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          result.ok ? 'Notification settings saved.' : result.message!,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        border: Border.all(color: AppColors.border),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.notifications_active_outlined,
                  size: 19, color: AppColors.brand),
              const SizedBox(width: 8),
              Text(
                'Notification settings',
                style: Theme.of(context).textTheme.titleSmall?.copyWith(
                      fontWeight: FontWeight.w600,
                      color: AppColors.ink,
                    ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          const Text(
            'Pick your district and we will email you whenever a coordinator '
            'confirms a High or Critical disaster there.',
            style: TextStyle(fontSize: 12.5, height: 1.5),
          ),
          const SizedBox(height: 16),

          const Text(
            'My district',
            style: TextStyle(
              fontSize: 12.5,
              fontWeight: FontWeight.w600,
              color: AppColors.ink,
            ),
          ),
          const SizedBox(height: 6),
          if (_loading)
            const _LoadingRow()
          else if (_loadError != null)
            _ErrorRow(message: _loadError!, onRetry: () {
              setState(() {
                _loading = true;
                _loadError = null;
              });
              _loadDistricts();
            })
          else
            _DistrictField(
              districts: _districts,
              value: _district,
              enabled: !_saving,
              onChanged: (value) => setState(() => _district = value),
            ),

          const SizedBox(height: 6),
          SwitchListTile.adaptive(
            value: _emailsOn,
            onChanged: _saving ? null : (value) => setState(() => _emailsOn = value),
            contentPadding: EdgeInsets.zero,
            dense: true,
            activeThumbColor: AppColors.brand,
            title: const Text(
              'Email me',
              style: TextStyle(
                fontSize: 13.5,
                fontWeight: FontWeight.w600,
                color: AppColors.ink,
              ),
            ),
            subtitle: const Text(
              'Covers both area warnings and receipts for reports you file.',
              style: TextStyle(fontSize: 12),
            ),
          ),

          if (_district == null && _emailsOn) ...[
            const SizedBox(height: 4),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Icon(Icons.info_outline, size: 15, color: AppColors.body),
                const SizedBox(width: 6),
                const Expanded(
                  child: Text(
                    'With no district set you will not receive area warnings.',
                    style: TextStyle(fontSize: 12, height: 1.4),
                  ),
                ),
              ],
            ),
          ],

          const SizedBox(height: 14),
          FilledButton(
            onPressed: (_dirty && !_saving && !_loading) ? _save : null,
            style: FilledButton.styleFrom(
              minimumSize: const Size.fromHeight(44),
              backgroundColor: AppColors.brand,
              foregroundColor: AppColors.brandInk,
            ),
            child: _saving
                ? const SizedBox(
                    height: 18,
                    width: 18,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: AppColors.brandInk,
                    ),
                  )
                : const Text('Save settings'),
          ),
        ],
      ),
    );
  }
}

/// The district picker. A plain dropdown rather than a form field: 25 options
/// is small enough to scroll and needs no validation of its own, because the
/// only values on offer come from the server.
class _DistrictField extends StatelessWidget {
  const _DistrictField({
    required this.districts,
    required this.value,
    required this.enabled,
    required this.onChanged,
  });

  final List<String> districts;
  final String? value;
  final bool enabled;
  final ValueChanged<String?> onChanged;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12),
      decoration: BoxDecoration(
        color: AppColors.surfaceAlt,
        border: Border.all(color: AppColors.border),
        borderRadius: BorderRadius.circular(10),
      ),
      child: DropdownButtonHideUnderline(
        child: DropdownButton<String?>(
          value: value,
          isExpanded: true,
          onChanged: enabled ? onChanged : null,
          borderRadius: BorderRadius.circular(10),
          style: const TextStyle(fontSize: 14, color: AppColors.ink),
          items: [
            const DropdownMenuItem<String?>(
              value: null,
              child: Text(
                'No district — do not warn me',
                style: TextStyle(fontSize: 14, color: AppColors.body),
              ),
            ),
            ...districts.map(
              (district) => DropdownMenuItem<String?>(
                value: district,
                child: Text(district, style: const TextStyle(fontSize: 14)),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _LoadingRow extends StatelessWidget {
  const _LoadingRow();

  @override
  Widget build(BuildContext context) {
    return const Row(
      children: [
        SizedBox(
          height: 15,
          width: 15,
          child: CircularProgressIndicator(strokeWidth: 2),
        ),
        SizedBox(width: 10),
        Text('Loading districts…', style: TextStyle(fontSize: 13)),
      ],
    );
  }
}

class _ErrorRow extends StatelessWidget {
  const _ErrorRow({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          message,
          style: const TextStyle(fontSize: 12.5, color: AppColors.critical),
        ),
        TextButton(
          onPressed: onRetry,
          style: TextButton.styleFrom(padding: EdgeInsets.zero),
          child: const Text('Try again'),
        ),
      ],
    );
  }
}
