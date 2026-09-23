import 'package:flutter/material.dart';

import '../services/resource_api.dart';

void main() {
  runApp(const RescueSriLankaApp());
}

class RescueSriLankaApp extends StatelessWidget {
  const RescueSriLankaApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'Rescue Sri Lanka',
        debugShowCheckedModeBanner: false,
        theme: ThemeData(
          colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xff0b6b68)),
          scaffoldBackgroundColor: const Color(0xfff4f8f7),
          inputDecorationTheme: const InputDecorationTheme(
            filled: true,
            fillColor: Colors.white,
            border: OutlineInputBorder(borderRadius: BorderRadius.all(Radius.circular(12)), borderSide: BorderSide.none),
          ),
          useMaterial3: true,
        ),
        home: const ResourceHomePage(),
      );
}

class MyApp extends RescueSriLankaApp {
  const MyApp({super.key});
}

class ResourceHomePage extends StatefulWidget {
  const ResourceHomePage({super.key});

  @override
  State<ResourceHomePage> createState() => _ResourceHomePageState();
}

class _ResourceHomePageState extends State<ResourceHomePage> {
  final _api = ResourceApi();
  int _selectedIndex = 0;
  List<HelpRequest> _requests = [];
  bool _loadingRequests = true;

  @override
  void initState() {
    super.initState();
    _loadRequests();
  }

  Future<void> _loadRequests() async {
    try {
      final requests = await _api.getHelpRequests();
      if (mounted) setState(() => _requests = requests);
    } catch (_) {
      // The empty state remains useful when the API is not running locally.
    } finally {
      if (mounted) setState(() => _loadingRequests = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          title: const Text('Resources', style: TextStyle(fontWeight: FontWeight.w800)),
          actions: [IconButton(onPressed: _loadRequests, icon: const Icon(Icons.refresh), tooltip: 'Refresh requests')],
        ),
        body: SafeArea(
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
                child: SegmentedButton<int>(
                  segments: const [
                    ButtonSegment(value: 0, icon: Icon(Icons.volunteer_activism_outlined), label: Text('Resource request')),
                    ButtonSegment(value: 1, icon: Icon(Icons.inventory_2_outlined), label: Text('Donate')),
                  ],
                  selected: {_selectedIndex},
                  onSelectionChanged: (selection) => setState(() => _selectedIndex = selection.first),
                ),
              ),
              Expanded(
                child: IndexedStack(index: _selectedIndex, children: [RequestHelpPage(api: _api, requests: _requests, loadingRequests: _loadingRequests, onSubmitted: _loadRequests), DonatePage(api: _api)]),
              ),
            ],
          ),
        ),
        floatingActionButton: _selectedIndex == 0 && _requests.isNotEmpty
            ? FloatingActionButton.small(onPressed: _loadRequests, child: const Icon(Icons.sync))
            : null,
      );
}

class RequestHelpPage extends StatefulWidget {
  const RequestHelpPage({required this.api, required this.requests, required this.loadingRequests, required this.onSubmitted, super.key});

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
    _name.dispose();
    _phone.dispose();
    _description.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() => _submitting = true);
    try {
      await widget.api.createHelpRequest(name: _name.text, phone: _phone.text, needType: _needType, description: _description.text);
      if (!mounted) return;
      _description.clear();
      await widget.onSubmitted();
      _showMessage('Your request was sent to the resource manager.');
    } catch (error) {
      _showMessage(error.toString().replaceFirst('Exception: ', ''), isError: true);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  void _showMessage(String message, {bool isError = false}) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message), backgroundColor: isError ? Colors.red.shade700 : null));

  @override
  Widget build(BuildContext context) => ListView(
        padding: const EdgeInsets.fromLTRB(20, 20, 20, 96),
        children: [
          const _PageIntro(icon: Icons.health_and_safety, title: 'What do you need?', subtitle: 'Tell our resource team what support is needed. Every request is reviewed by the admin team.'),
          const SizedBox(height: 20),
          Form(
            key: _formKey,
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              _field(_name, 'Your name', Icons.person_outline),
              const SizedBox(height: 12),
              _field(_phone, 'Contact number', Icons.phone_outlined, keyboardType: TextInputType.phone),
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                initialValue: _needType,
                decoration: const InputDecoration(labelText: 'Type of help needed', prefixIcon: Icon(Icons.category_outlined)),
                items: const ['Food and water', 'Medical aid', 'Rescue', 'Shelter', 'Other'].map((value) => DropdownMenuItem(value: value, child: Text(value))).toList(),
                onChanged: (value) => setState(() => _needType = value!),
              ),
              const SizedBox(height: 12),
              TextFormField(controller: _description, maxLines: 4, decoration: const InputDecoration(labelText: 'Describe what is needed', alignLabelWithHint: true, prefixIcon: Icon(Icons.notes_outlined)), validator: (value) => value == null || value.trim().isEmpty ? 'Please describe the need.' : null),
              const SizedBox(height: 18),
              FilledButton.icon(onPressed: _submitting ? null : _submit, icon: const Icon(Icons.send), label: Text(_submitting ? 'Sending request...' : 'Send request'), style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(vertical: 16))),
            ]),
          ),
          const SizedBox(height: 28),
          const Text('Your requests', style: TextStyle(fontSize: 20, fontWeight: FontWeight.w800)),
          const SizedBox(height: 10),
          if (widget.loadingRequests) const Center(child: CircularProgressIndicator()),
          if (!widget.loadingRequests && widget.requests.isEmpty) const Text('Submitted requests appear here with their latest status.', style: TextStyle(color: Colors.black54)),
          ...widget.requests.map((request) => Card(margin: const EdgeInsets.only(top: 10), child: ListTile(leading: const Icon(Icons.assignment_outlined), title: Text(request.needType), subtitle: Text(request.description), trailing: Chip(label: Text(request.status))))),
        ],
      );

  Widget _field(TextEditingController controller, String label, IconData icon, {TextInputType? keyboardType}) => TextFormField(controller: controller, keyboardType: keyboardType, decoration: InputDecoration(labelText: label, prefixIcon: Icon(icon)), validator: (value) => value == null || value.trim().isEmpty ? 'This field is required.' : null);
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
    _name.dispose();
    _phone.dispose();
    _quantity.dispose();
    _unit.dispose();
    _notes.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() => _submitting = true);
    try {
      await widget.api.createDonation(name: _name.text, phone: _phone.text, donationType: _donationType, quantity: double.parse(_quantity.text), unit: _unit.text, notes: _notes.text);
      if (!mounted) return;
      _quantity.clear();
      _unit.clear();
      _notes.clear();
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Thank you. The resource manager will contact you.')));
    } catch (error) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.toString().replaceFirst('Exception: ', '')), backgroundColor: Colors.red.shade700));
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => ListView(
        padding: const EdgeInsets.fromLTRB(20, 20, 20, 96),
        children: [
          const _PageIntro(icon: Icons.volunteer_activism, title: 'Give what you can', subtitle: 'Offer food, water, medical supplies or other resources directly to the resource manager.'),
          const SizedBox(height: 20),
          Form(
            key: _formKey,
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              _field(_name, 'Your name', Icons.person_outline),
              const SizedBox(height: 12),
              _field(_phone, 'Contact number', Icons.phone_outlined, keyboardType: TextInputType.phone),
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(initialValue: _donationType, decoration: const InputDecoration(labelText: 'What are you donating?', prefixIcon: Icon(Icons.category_outlined)), items: const ['Food and water', 'Medical supplies', 'Clothing', 'Other'].map((value) => DropdownMenuItem(value: value, child: Text(value))).toList(), onChanged: (value) => setState(() => _donationType = value!)),
              const SizedBox(height: 12),
              Row(children: [Expanded(child: _field(_quantity, 'Quantity', Icons.numbers, keyboardType: const TextInputType.numberWithOptions(decimal: true))), const SizedBox(width: 12), Expanded(child: _field(_unit, 'Unit (kg, boxes...)', Icons.straighten))]),
              const SizedBox(height: 12),
              TextFormField(controller: _notes, maxLines: 3, decoration: const InputDecoration(labelText: 'Notes (optional)', alignLabelWithHint: true, prefixIcon: Icon(Icons.notes_outlined))),
              const SizedBox(height: 18),
              FilledButton.icon(onPressed: _submitting ? null : _submit, icon: const Icon(Icons.volunteer_activism), label: Text(_submitting ? 'Sending donation...' : 'Offer donation'), style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(vertical: 16))),
            ]),
          ),
          const SizedBox(height: 20),
          const Card(child: Padding(padding: EdgeInsets.all(16), child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [Icon(Icons.info_outline), SizedBox(width: 12), Expanded(child: Text('Your offer is sent to the admin resource manager for review and coordination.'))]))),
        ],
      );

  Widget _field(TextEditingController controller, String label, IconData icon, {TextInputType? keyboardType}) => TextFormField(controller: controller, keyboardType: keyboardType, decoration: InputDecoration(labelText: label, prefixIcon: Icon(icon)), validator: (value) => value == null || value.trim().isEmpty ? 'This field is required.' : null);
}

class _PageIntro extends StatelessWidget {
  const _PageIntro({required this.icon, required this.title, required this.subtitle});

  final IconData icon;
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(20),
        decoration: BoxDecoration(color: const Color(0xffd9eeeb), borderRadius: BorderRadius.circular(22)),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Icon(icon, size: 34, color: const Color(0xff0b6b68)), const SizedBox(height: 14), Text(title, style: const TextStyle(fontSize: 28, fontWeight: FontWeight.w900)), const SizedBox(height: 6), Text(subtitle, style: const TextStyle(height: 1.4, color: Colors.black54))]),
      );
}
