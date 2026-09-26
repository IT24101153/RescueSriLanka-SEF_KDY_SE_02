import 'dart:io';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/theme.dart';
import '../../services/auth_service.dart';
import '../auth/login_screen.dart';
import '../auth/register_screen.dart';
import 'profile_edit_form.dart';
import '../../../shared/widgets/app_ui.dart';

/// Profile tab. Shows who is signed in, or offers the two ways to get there.
class ProfileScreen extends StatelessWidget {
  const ProfileScreen({super.key, required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: auth,
      builder: (context, _) {
        return Scaffold(
          appBar: const AppHeader(title: 'Profile'),
          body: SafeArea(
            child: auth.isSignedIn
                ? _SignedIn(auth: auth)
                : _SignedOut(auth: auth),
          ),
        );
      },
    );
  }
}

class _SignedIn extends StatefulWidget {
  const _SignedIn({required this.auth});

  final AuthService auth;

  @override
  State<_SignedIn> createState() => _SignedInState();
}

class _SignedInState extends State<_SignedIn> {
  bool _uploadingPhoto = false;

  Future<void> _changePhoto() async {
    final source = await showModalBottomSheet<ImageSource>(
      context: context,
      builder: (sheetContext) => SafeArea(
        child: Wrap(
          children: [
            ListTile(
              leading: const Icon(Icons.photo_camera_outlined),
              title: const Text('Take a photo'),
              onTap: () => Navigator.of(sheetContext).pop(ImageSource.camera),
            ),
            ListTile(
              leading: const Icon(Icons.photo_library_outlined),
              title: const Text('Choose from gallery'),
              onTap: () => Navigator.of(sheetContext).pop(ImageSource.gallery),
            ),
          ],
        ),
      ),
    );

    if (source == null || !mounted) return;

    File picked;
    try {
      final file = await ImagePicker().pickImage(
        source: source,
        // A profile photo never needs to be larger than it will ever be shown.
        maxWidth: 1024,
        imageQuality: 85,
      );
      if (file == null || !mounted) return;
      picked = File(file.path);
    } catch (_) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Could not open the camera on this device.'),
        ),
      );
      return;
    }

    setState(() => _uploadingPhoto = true);
    final result = await widget.auth.updatePhoto(picked);
    if (!mounted) return;
    setState(() => _uploadingPhoto = false);

    if (!result.ok) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(result.message!)));
    }
  }

  @override
  Widget build(BuildContext context) {
    final user = widget.auth.user!;
    final photoUrl = user.resolvedPhotoUrl;

    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 28),
      children: [
        Center(
          child: Stack(
            clipBehavior: Clip.none,
            children: [
              CircleAvatar(
                radius: 44,
                backgroundColor: AppColors.brand.withValues(alpha: 0.2),
                backgroundImage: photoUrl != null
                    ? NetworkImage(photoUrl)
                    : null,
                child: photoUrl == null
                    ? Text(
                        user.shortName.characters.first.toUpperCase(),
                        style: const TextStyle(
                          fontSize: 32,
                          fontWeight: FontWeight.w600,
                          color: AppColors.brandInk,
                        ),
                      )
                    : null,
              ),
              if (_uploadingPhoto)
                const Positioned.fill(
                  child: CircleAvatar(
                    backgroundColor: Colors.black45,
                    child: SizedBox(
                      height: 22,
                      width: 22,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: Colors.white,
                      ),
                    ),
                  ),
                ),
              Positioned(
                bottom: -2,
                right: -2,
                child: Material(
                  color: AppColors.brand,
                  shape: const CircleBorder(),
                  child: InkWell(
                    customBorder: const CircleBorder(),
                    onTap: _uploadingPhoto ? null : _changePhoto,
                    child: const Padding(
                      padding: EdgeInsets.all(7),
                      child: Icon(
                        Icons.camera_alt,
                        size: 16,
                        color: AppColors.brandInk,
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 14),
        Center(
          child: Text(
            user.fullName,
            style: const TextStyle(
              fontSize: 18,
              fontWeight: FontWeight.w600,
              color: AppColors.ink,
            ),
          ),
        ),
        const SizedBox(height: 2),
        Center(
          child: Text(
            user.email,
            style: const TextStyle(fontSize: 13, color: AppColors.body),
          ),
        ),

        const SizedBox(height: 24),
        ProfileEditForm(auth: widget.auth),

        const SizedBox(height: 28),
        OutlinedButton.icon(
          onPressed: () async {
            final messenger = ScaffoldMessenger.of(context);
            await widget.auth.signOut();
            messenger.showSnackBar(
              const SnackBar(content: Text('Signed out.')),
            );
          },
          style: OutlinedButton.styleFrom(
            minimumSize: const Size.fromHeight(46),
            foregroundColor: AppColors.critical,
            side: BorderSide(color: AppColors.critical.withValues(alpha: 0.4)),
          ),
          icon: const Icon(Icons.logout, size: 19),
          label: const Text('Sign out'),
        ),
      ],
    );
  }
}

class _SignedOut extends StatelessWidget {
  const _SignedOut({required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 28, 20, 28),
      children: [
        const Icon(Icons.person_outline, size: 52, color: AppColors.brand),
        const SizedBox(height: 18),
        Text(
          'You are browsing as a guest',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.titleLarge?.copyWith(
                fontWeight: FontWeight.w600,
                color: AppColors.ink,
              ),
        ),
        const SizedBox(height: 10),
        const Text(
          'The live map and safety zones are open to everyone. An account is '
          'needed only to file a report, so a coordinator can follow it up.',
          textAlign: TextAlign.center,
          style: TextStyle(fontSize: 13.5, height: 1.5),
        ),
        const SizedBox(height: 28),
        FilledButton(
          onPressed: () => Navigator.of(context).push<bool>(
            MaterialPageRoute(builder: (_) => LoginScreen(auth: auth)),
          ),
          style: FilledButton.styleFrom(
            minimumSize: const Size.fromHeight(48),
            backgroundColor: AppColors.brand,
            foregroundColor: AppColors.brandInk,
          ),
          child: const Text('Sign in'),
        ),
        const SizedBox(height: 12),
        OutlinedButton(
          onPressed: () => Navigator.of(context).push<bool>(
            MaterialPageRoute(builder: (_) => RegisterScreen(auth: auth)),
          ),
          style: OutlinedButton.styleFrom(
            minimumSize: const Size.fromHeight(48),
          ),
          child: const Text('Create an account'),
        ),
      ],
    );
  }
}
