import 'package:flutter/material.dart';

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

  @override
  void dispose() {
    for (final controller in [_name, _email, _phone, _password, _confirm]) {
      controller.dispose();
    }
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
