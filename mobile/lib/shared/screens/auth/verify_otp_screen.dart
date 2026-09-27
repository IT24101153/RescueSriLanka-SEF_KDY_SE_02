import 'package:flutter/material.dart';

import '../../core/theme.dart';
import '../../services/auth_service.dart';
import 'forgot_password_screen.dart';
import 'reset_password_screen.dart';

/// Second step of the forgot-password flow: the 6-digit code that was just
/// emailed to [email]. A correct code earns a reset token, which is handed
/// straight to [ResetPasswordScreen].
class VerifyOtpScreen extends StatefulWidget {
  const VerifyOtpScreen({super.key, required this.auth, required this.email});

  final AuthService auth;
  final String email;

  @override
  State<VerifyOtpScreen> createState() => _VerifyOtpScreenState();
}

class _VerifyOtpScreenState extends State<VerifyOtpScreen> {
  final _formKey = GlobalKey<FormState>();
  final _code = TextEditingController();

  bool _busy = false;
  bool _resending = false;
  String? _error;
  String? _info;

  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    setState(() {
      _busy = true;
      _error = null;
      _info = null;
    });

    final result = await widget.auth.verifyResetCode(
      email: widget.email,
      code: _code.text.trim(),
    );

    if (!mounted) return;

    if (!result.ok || result.resetToken == null) {
      setState(() {
        _busy = false;
        _error = result.message;
      });
      return;
    }

    final done = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => ResetPasswordScreen(
          auth: widget.auth,
          email: widget.email,
          resetToken: result.resetToken!,
        ),
      ),
    );

    if (!mounted) return;

    setState(() => _busy = false);

    if (done == true) {
      Navigator.of(context).pop(true);
    }
  }

  Future<void> _resend() async {
    setState(() {
      _resending = true;
      _error = null;
      _info = null;
    });

    final result = await widget.auth.forgotPassword(email: widget.email);

    if (!mounted) return;

    setState(() {
      _resending = false;
      if (result.ok) {
        _info = 'A new code has been sent to ${widget.email}.';
      } else {
        _error = result.message;
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final busy = _busy || _resending;

    return Scaffold(
      appBar: AppBar(title: const Text('Enter code')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 28),
          children: [
            Text(
              'Check your email',
              style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                    fontWeight: FontWeight.w600,
                    color: AppColors.ink,
                  ),
            ),
            const SizedBox(height: 6),
            Text(
              'Enter the 6-digit code we sent to ${widget.email}. '
              'It expires in 10 minutes.',
              style: const TextStyle(fontSize: 13.5, height: 1.45),
            ),
            const SizedBox(height: 24),

            if (_error != null) ...[
              ErrorBanner(message: _error!),
              const SizedBox(height: 16),
            ],
            if (_info != null) ...[
              _InfoBanner(message: _info!),
              const SizedBox(height: 16),
            ],

            Form(
              key: _formKey,
              child: TextFormField(
                controller: _code,
                keyboardType: TextInputType.number,
                textAlign: TextAlign.center,
                maxLength: 6,
                textInputAction: TextInputAction.done,
                onFieldSubmitted: (_) => _submit(),
                style: const TextStyle(
                  fontSize: 24,
                  letterSpacing: 8,
                  fontWeight: FontWeight.w600,
                ),
                decoration: const InputDecoration(
                  counterText: '',
                  labelText: '6-digit code',
                  border: OutlineInputBorder(),
                ),
                validator: (value) {
                  final text = value?.trim() ?? '';
                  if (text.length != 6 || int.tryParse(text) == null) {
                    return 'Enter the 6-digit code from your email.';
                  }
                  return null;
                },
              ),
            ),

            const SizedBox(height: 22),
            FilledButton(
              onPressed: busy ? null : _submit,
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
                  : const Text('Verify code'),
            ),

            const SizedBox(height: 14),
            Center(
              child: TextButton(
                onPressed: busy ? null : _resend,
                child: _resending
                    ? const SizedBox(
                        width: 16,
                        height: 16,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Text("Didn't get a code? Resend"),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _InfoBanner extends StatelessWidget {
  const _InfoBanner({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppColors.low.withValues(alpha: 0.08),
        border: Border.all(color: AppColors.low.withValues(alpha: 0.3)),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(Icons.check_circle_outline, size: 19, color: AppColors.low),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              message,
              style: const TextStyle(fontSize: 13, height: 1.4, color: AppColors.low),
            ),
          ),
        ],
      ),
    );
  }
}
