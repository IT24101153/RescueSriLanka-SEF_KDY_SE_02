import 'package:flutter/material.dart';

import '../../../features/component_a/services/api_client.dart';
import '../../core/theme.dart';
import '../../services/auth_service.dart';

/// Citizen self-registration, posting to /api/auth/register.
///
/// The API only ever creates Citizen accounts here — coordinator and resource
/// manager accounts are seeded, never self-served.
class RegisterScreen extends StatefulWidget {
  const RegisterScreen({super.key, required this.auth});

  final AuthService auth;

  @override
  State<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends State<RegisterScreen> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _email = TextEditingController();
  final _phone = TextEditingController();
  final _password = TextEditingController();
  final _confirm = TextEditingController();

  bool _obscure = true;
  bool _agreed = false;
  bool _busy = false;
  String? _error;

  /// Where the person lives, so district warnings can reach them. The list is
  /// the server's, and the whole field is optional — an account still works
  /// without one, and it can be set later from the profile.
  final ApiClient _api = ApiClient.anonymous();
  List<String> _districts = [];
  String? _district;

  @override
  void initState() {
    super.initState();
    _loadDistricts();
  }

  Future<void> _loadDistricts() async {
    try {
      final districts = await _api.fetchDistricts();
      if (mounted) setState(() => _districts = districts);
    } catch (_) {
      // Registration matters more than the picker; leave it hidden.
    }
  }

  @override
  void dispose() {
    for (final controller in [_name, _email, _phone, _password, _confirm]) {
      controller.dispose();
    }
    _api.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    if (!_agreed) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Please accept the terms to continue.')),
      );
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    final result = await widget.auth.register(
      fullName: _name.text,
      email: _email.text,
      password: _password.text,
      phoneNumber: _phone.text,
    );

    if (!mounted) return;

    if (result.ok) {
      // Registration only takes name, email, password and phone, so the
      // district is saved straight after with the token it just returned.
      if (_district != null) {
        await widget.auth.updatePreferences(district: _district);
      }
      if (!mounted) return;
      // Registration returns a token, so there is no second sign-in step.
      Navigator.of(context).pop(true);
      return;
    }

    setState(() {
      _busy = false;
      _error = result.message;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Create account')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 28),
          children: [
            Text(
              'Create your account',
              style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                    fontWeight: FontWeight.w600,
                    color: AppColors.ink,
                  ),
            ),
            const SizedBox(height: 6),
            const Text(
              'Your phone number lets a coordinator reach you about a report '
              'you filed. It is never shown on the public map.',
              style: TextStyle(fontSize: 13.5, height: 1.45),
            ),
            const SizedBox(height: 24),

            if (_error != null) ...[
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: AppColors.critical.withValues(alpha: 0.08),
                  border:
                      Border.all(color: AppColors.critical.withValues(alpha: 0.3)),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Icon(Icons.error_outline,
                        size: 19, color: AppColors.critical),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        _error!,
                        style: const TextStyle(
                          fontSize: 13,
                          height: 1.4,
                          color: AppColors.critical,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 16),
            ],

            Form(
              key: _formKey,
              child: Column(
                children: [
                  TextFormField(
                    controller: _name,
                    textCapitalization: TextCapitalization.words,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Full name',
                      border: OutlineInputBorder(),
                      prefixIcon: Icon(Icons.person_outline),
                    ),
                    validator: (value) => (value?.trim() ?? '').isEmpty
                        ? 'Enter your name.'
                        : null,
                  ),
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _email,
                    keyboardType: TextInputType.emailAddress,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Email',
                      border: OutlineInputBorder(),
                      prefixIcon: Icon(Icons.mail_outline),
                    ),
                    validator: (value) {
                      final text = value?.trim() ?? '';
                      if (text.isEmpty) return 'Enter your email address.';
                      if (!text.contains('@') || !text.contains('.')) {
                        return 'Enter a valid email address.';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _phone,
                    keyboardType: TextInputType.phone,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Phone number (optional)',
                      border: OutlineInputBorder(),
                      prefixIcon: Icon(Icons.phone_outlined),
                    ),
                  ),
                  if (_districts.isNotEmpty) ...[
                    const SizedBox(height: 14),
                    _DistrictField(
                      districts: _districts,
                      value: _district,
                      enabled: !_busy,
                      onChanged: (value) => setState(() => _district = value),
                    ),
                  ],
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _password,
                    obscureText: _obscure,
                    textInputAction: TextInputAction.next,
                    decoration: InputDecoration(
                      labelText: 'Password',
                      border: const OutlineInputBorder(),
                      prefixIcon: const Icon(Icons.lock_outline),
                      // The API enforces eight characters; say so before the
                      // server has to.
                      helperText: 'At least 8 characters',
                      suffixIcon: IconButton(
                        icon: Icon(_obscure
                            ? Icons.visibility_outlined
                            : Icons.visibility_off_outlined),
                        onPressed: () => setState(() => _obscure = !_obscure),
                        tooltip: _obscure ? 'Show password' : 'Hide password',
                      ),
                    ),
                    validator: (value) => (value ?? '').length < 8
                        ? 'Use at least 8 characters.'
                        : null,
                  ),
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _confirm,
                    obscureText: _obscure,
                    onFieldSubmitted: (_) => _submit(),
                    decoration: const InputDecoration(
                      labelText: 'Confirm password',
                      border: OutlineInputBorder(),
                      prefixIcon: Icon(Icons.lock_reset_outlined),
                    ),
                    validator: (value) => value != _password.text
                        ? 'The passwords do not match.'
                        : null,
                  ),
                ],
              ),
            ),

            const SizedBox(height: 10),
            CheckboxListTile(
              value: _agreed,
              onChanged: (value) => setState(() => _agreed = value ?? false),
              controlAffinity: ListTileControlAffinity.leading,
              contentPadding: EdgeInsets.zero,
              activeColor: AppColors.brand,
              checkColor: AppColors.brandInk,
              title: const Text(
                'I agree that reports I submit may be shared with emergency '
                'coordinators.',
                style: TextStyle(fontSize: 13, height: 1.4),
              ),
            ),

            const SizedBox(height: 14),
            FilledButton(
              onPressed: _busy ? null : _submit,
              style: FilledButton.styleFrom(
                minimumSize: const Size.fromHeight(48),
                backgroundColor: AppColors.brand,
                foregroundColor: AppColors.brandInk,
              ),
              child: _busy
                  ? const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(strokeWidth: 2.2),
                    )
                  : const Text('Create account'),
            ),
          ],
        ),
      ),
    );
  }
}

/// District picker for sign-up: a labelled dropdown whose only values come
/// from the server, with an explicit "prefer not to say" entry so the field
/// stays optional.
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
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 12),
          decoration: BoxDecoration(
            border: Border.all(color: AppColors.border),
            borderRadius: BorderRadius.circular(4),
          ),
          child: Row(
            children: [
              const Icon(Icons.place_outlined, color: AppColors.body, size: 22),
              const SizedBox(width: 12),
              Expanded(
                child: DropdownButtonHideUnderline(
                  child: DropdownButton<String?>(
                    value: value,
                    isExpanded: true,
                    onChanged: enabled ? onChanged : null,
                    borderRadius: BorderRadius.circular(10),
                    style: const TextStyle(fontSize: 16, color: AppColors.ink),
                    hint: const Text(
                      'Where do you live?',
                      style: TextStyle(fontSize: 16, color: AppColors.body),
                    ),
                    items: [
                      const DropdownMenuItem<String?>(
                        value: null,
                        child: Text(
                          'Prefer not to say',
                          style: TextStyle(fontSize: 15, color: AppColors.body),
                        ),
                      ),
                      ...districts.map(
                        (district) => DropdownMenuItem<String?>(
                          value: district,
                          child: Text(
                            district,
                            style: const TextStyle(fontSize: 15),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
        const Padding(
          padding: EdgeInsets.fromLTRB(12, 6, 12, 0),
          child: Text(
            'Your district. We use it to warn you when a disaster is reported '
            'near you.',
            style: TextStyle(fontSize: 12, color: AppColors.body, height: 1.35),
          ),
        ),
      ],
    );
  }
}
