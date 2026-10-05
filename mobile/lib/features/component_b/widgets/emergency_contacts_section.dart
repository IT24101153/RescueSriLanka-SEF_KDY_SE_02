import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/widgets/app_ui.dart';
import '../services/help_request_service.dart';

const _categoryTitles = {
  'DistrictDisaster': 'Your district',
  'CentralRescue': 'Disaster rescue',
  'Medical': 'Medical',
  'Fire': 'Fire and rescue',
  'Police': 'Police',
  'Authority': 'Authorities and advice',
};

const _categoryIcons = {
  'DistrictDisaster': Icons.location_on_outlined,
  'CentralRescue': Icons.support_outlined,
  'Medical': Icons.medical_services_outlined,
  'Fire': Icons.local_fire_department_outlined,
  'Police': Icons.local_police_outlined,
  'Authority': Icons.info_outline,
};

/// The emergency numbers for a place, grouped, each with a tap-to-call button.
/// [load] fetches them, so the same list serves one request's location or a
/// general lookup.
class EmergencyContactsSection extends StatefulWidget {
  const EmergencyContactsSection({super.key, required this.load});

  final Future<EmergencyContacts?> Function() load;

  @override
  State<EmergencyContactsSection> createState() =>
      _EmergencyContactsSectionState();
}

class _EmergencyContactsSectionState extends State<EmergencyContactsSection> {
  EmergencyContacts? _contacts;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    final result = await widget.load();
    if (!mounted) return;
    setState(() {
      _contacts = result;
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

    final contacts = _contacts;
    if (contacts == null) {
      return AppErrorBanner(
        message: 'Could not load emergency numbers.',
        onRetry: _load,
      );
    }

    final groups = <String, List<EmergencyContact>>{};
    for (final contact in contacts.contacts) {
      groups.putIfAbsent(contact.category, () => []).add(contact);
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          contacts.matchedDistrict == null
              ? 'National numbers. No local district unit matched this area.'
              : 'Includes the ${contacts.matchedDistrict} district disaster '
                    'management unit.',
          style: const TextStyle(
            fontSize: 12.5,
            height: 1.4,
            color: AppColors.body,
          ),
        ),
        for (final entry in groups.entries) ...[
          const SizedBox(height: 16),
          Row(
            children: [
              Icon(
                _categoryIcons[entry.key] ?? Icons.phone_outlined,
                size: 16,
                color: AppColors.brandInk,
              ),
              const SizedBox(width: 6),
              Text(
                _categoryTitles[entry.key] ?? entry.key,
                style: const TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.w700,
                  color: AppColors.ink,
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          for (final contact in entry.value) ...[
            _ContactCard(contact: contact),
            const SizedBox(height: AppSpacing.gap),
          ],
        ],
      ],
    );
  }
}

class _ContactCard extends StatelessWidget {
  const _ContactCard({required this.contact});

  final EmergencyContact contact;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  contact.name,
                  style: const TextStyle(
                    fontWeight: FontWeight.w700,
                    fontSize: 14,
                    color: AppColors.ink,
                  ),
                ),
                if (contact.description != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    contact.description!,
                    style: const TextStyle(
                      fontSize: 12.5,
                      height: 1.3,
                      color: AppColors.body,
                    ),
                  ),
                ],
                if (contact.isAvailable24x7) ...[
                  const SizedBox(height: 6),
                  const AppPill('24/7', tone: AppColors.safe),
                ],
              ],
            ),
          ),
          const SizedBox(width: 12),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              _CallButton(number: contact.phoneNumber),
              if (contact.secondaryPhoneNumber != null) ...[
                const SizedBox(height: 6),
                _CallButton(number: contact.secondaryPhoneNumber!),
              ],
            ],
          ),
        ],
      ),
    );
  }
}

class _CallButton extends StatelessWidget {
  const _CallButton({required this.number});

  final String number;

  Future<void> _call(BuildContext context) async {
    final launched = await launchUrl(Uri(scheme: 'tel', path: number));
    if (!launched && context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Could not start a call. Dial $number.')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return OutlinedButton.icon(
      onPressed: () => _call(context),
      icon: const Icon(Icons.call, size: 16),
      label: Text(number),
      style: OutlinedButton.styleFrom(
        visualDensity: VisualDensity.compact,
        minimumSize: const Size(0, 36),
      ),
    );
  }
}
